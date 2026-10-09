using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Dnkhw.Core.Downloads;

/// <summary>
/// 默认路径换盘：六个已知文件夹（桌面/文档/下载/图片/音乐/视频）的状态采集、
/// 文件迁移与已知文件夹重定向。顺序保证：先完成文件迁移，再写注册表并通知 Explorer——
/// 中断时不会出现「指针已改文件未动」的状态。
/// </summary>
public sealed class KnownFoldersMigrationService
{
    private const string UserShellFoldersKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";

    private static readonly JsonSerializerOptions StateJsonOptions = new()
    {
        WriteIndented = true,
    };

    private static string StateDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dnkhw");

    private static string StateFile => Path.Combine(StateDirectory, "known-folders-migration.json");

    /// <summary>可迁移的六个已知文件夹。</summary>
    public static IReadOnlyList<KnownFolderDef> Folders =
    [
        new KnownFolderDef("downloads", "下载", KnownFolders.DownloadsId, "Downloads",
            "{374DE290-123F-4565-9164-39C4925E467B}", null),
        new KnownFolderDef("documents", "文档", KnownFolders.DocumentsId, "Documents",
            "{F42EE2D3-909F-4907-8871-4C22FC0BF756}", "Personal"),
        new KnownFolderDef("desktop", "桌面", KnownFolders.DesktopId, "Desktop",
            "Desktop", null),
        new KnownFolderDef("pictures", "图片", KnownFolders.PicturesId, "Pictures",
            "{0DDD015D-B06C-45D5-8C4C-F59713854639}", "My Pictures"),
        new KnownFolderDef("music", "音乐", KnownFolders.MusicId, "Music",
            "{A0C69A99-21C8-4671-8703-7934162FCF1D}", "My Music"),
        new KnownFolderDef("videos", "视频", KnownFolders.VideosId, "Videos",
            "{35286A68-3C57-41A1-BBB1-0EAE73D76C95}", "My Video"),
    ];

    public static KnownFolderDef FindDef(string key)
        => Folders.FirstOrDefault(f => f.Key == key)
            ?? throw new ArgumentOutOfRangeException(nameof(key), $"未知文件夹：{key}");

    /// <summary>
    /// 读取 User Shell Folders 中 Explorer 实际使用的指向（真相源）：
    /// GUID 值名优先（现代重定向，含 OneDrive 接管），旧版名称值兜底；均无值返回空串。
    /// </summary>
    private static string ReadRegistryValue(KnownFolderDef def)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UserShellFoldersKey);
            foreach (var name in new[] { def.PrimaryValueName, def.FallbackValueName })
            {
                if (name is null)
                {
                    continue;
                }

                if (key?.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // 读取失败回退 SHGetKnownFolderPath
        }

        return string.Empty;
    }

    /// <summary>读取单个文件夹状态：当前路径、是否重定向、内容统计、OneDrive 接管检测、可还原记录。</summary>
    public KnownFolderStatus GetStatus(KnownFolderDef def)
    {
        // 真相源优先：User Shell Folders 的 GUID/旧名值（Explorer 实际行为）；
        // 无注册表值时用 SHGetKnownFolderPath 标准解析（含缺失文件夹创建）
        var registryValue = ReadRegistryValue(def);
        var shellPath = KnownFolders.GetKnownFolderPath(def.Id);
        var current = !string.IsNullOrWhiteSpace(registryValue) ? registryValue : shellPath;
        var defaultPath = def.DefaultPath;
        var isRedirected = !string.Equals(current, defaultPath, StringComparison.OrdinalIgnoreCase);
        var (count, bytes) = CountFiles(current);

        return new KnownFolderStatus(
            def,
            current,
            defaultPath,
            isRedirected,
            count,
            bytes,
            ReadState(def.Key),
            current.Contains("OneDrive", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 迁移到目标盘的对应目录（如 D:\Downloads）。仅当 moveFiles 为 true 时搬动文件；无论如何都会重定向注册表。
    /// </summary>
    public async Task<MigrationResult> MigrateAsync(
        KnownFolderDef def,
        string targetRoot,
        bool moveFiles,
        IProgress<MigrationProgress>? progress,
        CancellationToken ct = default)
    {
        var registryValue = ReadRegistryValue(def);
        var current = !string.IsNullOrWhiteSpace(registryValue) ? registryValue : KnownFolders.GetKnownFolderPath(def.Id);
        var target = Path.Combine(targetRoot, def.DefaultSubFolder);

        if (string.Equals(Path.GetPathRoot(current), Path.GetPathRoot(targetRoot), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标盘与当前文件夹在同一个分区，无需迁移");
        }

        var previousPath = current;
        List<FileFailure> failures = [];
        var moved = 0;

        if (moveFiles)
        {
            Directory.CreateDirectory(target);
            (moved, failures) = await MigrateDirectoryContentsAsync(current, target, progress, ct).ConfigureAwait(false);
        }

        // 文件步骤结束后再改注册表（见类注释的顺序保证）
        ApplyRegistryPath(def, target);
        SaveState(def.Key, previousPath);
        KnownFolders.NotifyFolderChanged(previousPath, target);

        return new MigrationResult(previousPath, target, moved, moveFiles, failures);
    }

    /// <summary>还原到迁移前的路径；无记录时还原系统默认位置（写 %USERPROFILE% 形式的标准默认值）。</summary>
    public void Restore(KnownFolderDef def)
    {
        var restoreTo = ReadState(def.Key) ?? def.DefaultPath;
        Directory.CreateDirectory(restoreTo);

        var current = KnownFolders.GetKnownFolderPath(def.Id);
        ApplyRegistryPath(def, restoreTo);
        ClearState(def.Key);
        KnownFolders.NotifyFolderChanged(current, restoreTo);
    }

    /// <summary>
    /// 将 source 下全部文件转移到 target（保留相对结构）。同盘 File.Move，跨盘 Copy+Delete；
    /// 目标已存在同名文件或文件被占用时跳过并记入失败清单。供单元测试直接调用。
    /// </summary>
    internal static async Task<(int Moved, List<FileFailure> Failures)> MigrateDirectoryContentsAsync(
        string source,
        string target,
        IProgress<MigrationProgress>? progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(target);
        var sameVolume = string.Equals(
            Path.GetPathRoot(Path.GetFullPath(source)),
            Path.GetPathRoot(Path.GetFullPath(target)),
            StringComparison.OrdinalIgnoreCase);

        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
        var relativeFiles = await Task.Run(() =>
        {
            var list = new List<string>();
            foreach (var f in Directory.EnumerateFiles(source, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                list.Add(Path.GetRelativePath(source, f));
            }

            return list;
        }, ct).ConfigureAwait(false);

        var failures = new List<FileFailure>();
        var moved = 0;
        await Task.Run(() =>
        {
            foreach (var rel in relativeFiles)
            {
                ct.ThrowIfCancellationRequested();
                var src = Path.Combine(source, rel);
                var dst = Path.Combine(target, rel);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    if (File.Exists(dst))
                    {
                        failures.Add(new FileFailure(rel, "目标已存在同名文件"));
                    }
                    else
                    {
                        if (sameVolume)
                        {
                            File.Move(src, dst);
                        }
                        else
                        {
                            File.Copy(src, dst);
                            File.Delete(src);
                        }

                        moved++;
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(new FileFailure(rel, ex.Message));
                    // 清理跨盘复制中断留下的半成品
                    try
                    {
                        if (!sameVolume && File.Exists(dst))
                        {
                            File.Delete(dst);
                        }
                    }
                    catch
                    {
                        // 清理失败无碍
                    }
                }

                progress?.Report(new MigrationProgress(moved + failures.Count, relativeFiles.Count, rel, failures.Count));
            }
        }, ct).ConfigureAwait(false);

        CleanupEmptyDirectories(source);
        return (moved, failures);
    }

    private static void CleanupEmptyDirectories(string root)
    {
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
            foreach (var dir in Directory.EnumerateDirectories(root, "*", options).OrderByDescending(p => p.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    try
                    {
                        Directory.Delete(dir);
                    }
                    catch
                    {
                        // 残留空目录无碍
                    }
                }
            }
        }
        catch
        {
            // 清理失败无碍
        }
    }

    private static (long Count, long Bytes) CountFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            return (0, 0);
        }

        long count = 0, bytes = 0;
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
            foreach (var f in Directory.EnumerateFiles(root, "*", options))
            {
                count++;
                try
                {
                    bytes += new FileInfo(f).Length;
                }
                catch
                {
                    // 单文件大小读取失败忽略
                }
            }
        }
        catch
        {
            // 目录不可读时返回已统计部分
        }

        return (count, bytes);
    }

    /// <summary>写入 Explorer 实际使用的值名（现代 GUID 优先；桌面为旧名 Desktop）。</summary>
    private static void ApplyRegistryPath(KnownFolderDef def, string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(UserShellFoldersKey, writable: true)
            ?? throw new InvalidOperationException("无法打开注册表 User Shell Folders");
        key.SetValue(def.PrimaryValueName, path, RegistryValueKind.ExpandString);
    }

    private sealed class MigrationStateMap
    {
        [JsonPropertyName("previousPaths")]
        public Dictionary<string, string> PreviousPaths { get; set; } = [];

        [JsonPropertyName("migratedAtUtc")]
        public DateTime MigratedAtUtc { get; set; } = DateTime.UtcNow;
    }

    private static Dictionary<string, string> ReadStateMap()
    {
        try
        {
            if (!File.Exists(StateFile))
            {
                return [];
            }

            var map = JsonSerializer.Deserialize<MigrationStateMap>(File.ReadAllText(StateFile), StateJsonOptions);
            return map?.PreviousPaths ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string? ReadState(string key)
    {
        return ReadStateMap().TryGetValue(key, out var path) ? path : null;
    }

    private static void SaveState(string key, string previousPath)
    {
        Directory.CreateDirectory(StateDirectory);
        var map = ReadStateMap();
        map[key] = previousPath;
        File.WriteAllText(StateFile, JsonSerializer.Serialize(new MigrationStateMap { PreviousPaths = map }, StateJsonOptions));
    }

    private static void ClearState(string key)
    {
        try
        {
            var map = ReadStateMap();
            if (!map.Remove(key))
            {
                return;
            }

            if (map.Count == 0)
            {
                File.Delete(StateFile);
            }
            else
            {
                File.WriteAllText(StateFile, JsonSerializer.Serialize(new MigrationStateMap { PreviousPaths = map }, StateJsonOptions));
            }
        }
        catch
        {
            // 状态文件清理失败不影响还原结果
        }
    }
}
