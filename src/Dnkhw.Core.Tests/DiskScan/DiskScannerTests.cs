using Dnkhw.Core.DiskScan;

namespace Dnkhw.Core.Tests.DiskScan;

public class DiskScannerTests : IDisposable
{
    private readonly string _root;

    public DiskScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dnkhw-scan-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "a", "sub"));
        Directory.CreateDirectory(Path.Combine(_root, "b"));
        File.WriteAllText(Path.Combine(_root, "a", "x.bin"), new string('x', 100));
        File.WriteAllText(Path.Combine(_root, "a", "sub", "y.bin"), new string('y', 50));
        File.WriteAllText(Path.Combine(_root, "b", "z.bin"), new string('z', 10));
        File.WriteAllText(Path.Combine(_root, "w.bin"), new string('w', 5));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
            // 临时目录清理失败无碍
        }
    }

    [Fact]
    public async Task Scan_AggregatesTopFoldersAndFiles()
    {
        var scanner = new DiskScanner();
        var result = await scanner.ScanAsync(_root, null);

        Assert.Equal(165, result.ScannedBytes);
        Assert.Equal(5, result.RootLooseBytes);
        Assert.Equal(4, result.ScannedFiles);
        Assert.Equal(0, result.SkippedEntries);

        // 一级目录占用含子级：a=150（150/165≈91%），b=10
        Assert.Equal(2, result.TopFolders.Count);
        Assert.Equal("a", Path.GetFileName(result.TopFolders[0].Path));
        Assert.Equal(150, result.TopFolders[0].Bytes);
        Assert.Equal(91, result.TopFolders[0].Percent);
        Assert.Equal(2, result.TopFolders[0].FileCount);
        Assert.Equal("b", Path.GetFileName(result.TopFolders[1].Path));
        Assert.Equal(10, result.TopFolders[1].Bytes);

        // 最大文件排序
        Assert.Equal("x.bin", Path.GetFileName(result.TopFiles[0].Path));
        Assert.Equal(100, result.TopFiles[0].Bytes);
        Assert.Equal("y.bin", Path.GetFileName(result.TopFiles[1].Path));
    }

    [Fact]
    public async Task Scan_ReportsProgress()
    {
        var scanner = new DiskScanner();
        var collector = new SyncProgress<long>();
        await scanner.ScanAsync(_root, collector);

        Assert.Contains(4L, collector.Values);
    }

    [Fact]
    public async Task Scan_Cancelled_Throws()
    {
        var scanner = new DiskScanner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(_root, null, cts.Token));
    }

    [Fact]
    public async Task Scan_MissingRoot_Throws()
    {
        var scanner = new DiskScanner();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => scanner.ScanAsync(Path.Combine(_root, "no-such-dir"), null));
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
