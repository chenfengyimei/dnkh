using Dnkhw.Core.Downloads;

namespace Dnkhw.Core.Tests.Downloads;

public class MigrationServiceTests : IDisposable
{
    private readonly string _source;
    private readonly string _target;

    public MigrationServiceTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "dnkhw-tests", Guid.NewGuid().ToString("N"));
        _source = Path.Combine(root, "src");
        _target = Path.Combine(root, "dst");
        Directory.CreateDirectory(_source);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_source)!, true);
        }
        catch
        {
            // 临时目录清理失败无碍
        }
    }

    [Fact]
    public async Task Migrate_MovesNestedStructure()
    {
        File.WriteAllText(Path.Combine(_source, "a.txt"), "a");
        Directory.CreateDirectory(Path.Combine(_source, "sub"));
        File.WriteAllText(Path.Combine(_source, "sub", "b.txt"), "b");
        File.WriteAllText(Path.Combine(_source, "c.bin"), "c");

        var (moved, failures) = await KnownFoldersMigrationService.MigrateDirectoryContentsAsync(_source, _target, null, CancellationToken.None);

        Assert.Equal(3, moved);
        Assert.Empty(failures);
        Assert.True(File.Exists(Path.Combine(_target, "a.txt")));
        Assert.True(File.Exists(Path.Combine(_target, "sub", "b.txt")));
        Assert.True(File.Exists(Path.Combine(_target, "c.bin")));
        Assert.False(File.Exists(Path.Combine(_source, "a.txt")));
        Assert.False(Directory.Exists(Path.Combine(_source, "sub")));
    }

    [Fact]
    public async Task Migrate_ConflictFileSkippedAndKept()
    {
        File.WriteAllText(Path.Combine(_source, "dup.txt"), "source");
        Directory.CreateDirectory(_target);
        File.WriteAllText(Path.Combine(_target, "dup.txt"), "existing");

        var (moved, failures) = await KnownFoldersMigrationService.MigrateDirectoryContentsAsync(_source, _target, null, CancellationToken.None);

        Assert.Equal(0, moved);
        var failure = Assert.Single(failures);
        Assert.Equal("dup.txt", failure.RelativePath);
        Assert.Equal("目标已存在同名文件", failure.Reason);
        Assert.Equal("source", await File.ReadAllTextAsync(Path.Combine(_source, "dup.txt")));
        Assert.Equal("existing", await File.ReadAllTextAsync(Path.Combine(_target, "dup.txt")));
    }

    [Fact]
    public async Task Migrate_LockedFileFailsOthersSucceed()
    {
        File.WriteAllText(Path.Combine(_source, "ok.txt"), "ok");
        var lockedPath = Path.Combine(_source, "locked.txt");
        await File.WriteAllTextAsync(lockedPath, "locked");

        using var _ = File.Open(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var (moved, failures) = await KnownFoldersMigrationService.MigrateDirectoryContentsAsync(_source, _target, null, CancellationToken.None);

        Assert.Equal(1, moved);
        var failure = Assert.Single(failures);
        Assert.Equal("locked.txt", failure.RelativePath);
        Assert.True(File.Exists(Path.Combine(_target, "ok.txt")));
        Assert.True(File.Exists(lockedPath));
    }

    [Fact]
    public async Task Migrate_ReportsProgress()
    {
        File.WriteAllText(Path.Combine(_source, "1.txt"), "1");
        File.WriteAllText(Path.Combine(_source, "2.txt"), "2");

        var collector = new SyncProgress<MigrationProgress>();
        await KnownFoldersMigrationService.MigrateDirectoryContentsAsync(_source, _target, collector, CancellationToken.None);

        Assert.NotEmpty(collector.Values);
        var last = collector.Values[^1];
        Assert.Equal(2, last.Completed);
        Assert.Equal(2, last.Total);
    }

    [Fact]
    public void Folders_SixDefinitionsAreIntact()
    {
        Assert.Equal(6, KnownFoldersMigrationService.Folders.Count);
        var keys = new HashSet<string>();
        var guids = new HashSet<Guid>();
        foreach (var def in KnownFoldersMigrationService.Folders)
        {
            Assert.True(keys.Add(def.Key), $"文件夹 Key 重复：{def.Key}");
            Assert.True(guids.Add(def.Id), $"文件夹 GUID 重复：{def.Id}");
            Assert.False(string.IsNullOrWhiteSpace(def.Name));
            Assert.False(string.IsNullOrWhiteSpace(def.DefaultSubFolder));
        }

        Assert.Contains(KnownFoldersMigrationService.Folders, f => f.Key == "downloads" && f.Name == "下载");
        Assert.Contains(KnownFoldersMigrationService.Folders, f => f.Key == "desktop" && f.Name == "桌面");
    }

    /// <summary>同步 IProgress（Progress&lt;T&gt; 经同步上下文异步投递，单测中无法保证时序）。</summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
