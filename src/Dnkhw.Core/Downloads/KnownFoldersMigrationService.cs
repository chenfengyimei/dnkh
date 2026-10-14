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
    private const string ShellFoldersKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders";
    private static readonly object StateLock = new();

    private static readonly JsonSerializerOptions StateJsonOptions = new()
    {
        WriteIndented = true,
    };

    private static string StateDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dnkhw");

    private static string StateFile => Path.Combine(StateDirectory, "known-folders-migration.json");

    /// <summary>可迁移的六个已知文件夹。</summary>
    public static readonly IReadOnlyList<KnownFolderDef> Folders =
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
                    return Environment.ExpandEnvironmentVariables(value.Trim());
                }
            }
        }
        catch
        {
            // 读取失败回退 SHGetKnownFolderPath
        }

        return string.Empty;
    }

    /// <summary>读取 Explorer 当前实际使用的路径，不执行文件统计。</summary>
    public string GetCurrentPath(KnownFolderDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        var registryValue = ReadRegistryValue(def);
        var path = !string.IsNullOrWhiteSpace(registryValue)
            ? registryValue
            : KnownFolders.GetKnownFolderPath(def.Id);
        if (!Path.IsPathFullyQualified(path)
            || path.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(@"\\?\GLOBALROOT\", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"已知文件夹「{def.Name}」的路径无效：{path}");
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    /// <summary>读取单个文件夹状态：当前路径、是否重定向、内容统计、OneDrive 接管检测、可还原记录。</summary>
    public KnownFolderStatus GetStatus(KnownFolderDef def)
    {
        // 真相源优先：User Shell Folders 的 GUID/旧名值（Explorer 实际行为）；
        // 无注册表值时用 SHGetKnownFolderPath 标准解析（含缺失文件夹创建）
        var current = GetCurrentPath(def);
        var defaultPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(def.DefaultPath));
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
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);
        var current = GetCurrentPath(def);
        var normalizedTargetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetRoot));
        var volumeRoot = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(normalizedTargetRoot)
            ?? throw new InvalidOperationException("目标位置不属于有效分区"));
        if (!string.Equals(normalizedTargetRoot, volumeRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标位置必须是磁盘根目录");
        }

        var target = Path.Combine(normalizedTargetRoot, def.DefaultSubFolder);

        if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标位置与当前文件夹相同，无需迁移");
        }

        if (string.Equals(Path.GetPathRoot(current), Path.GetPathRoot(normalizedTargetRoot), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标盘与当前文件夹在同一个分区，无需迁移");
        }

        if (moveFiles && IsProtectedMigrationSource(current))
        {
            throw new InvalidOperationException(
                $"当前路径 {current} 范围过大或属于系统保护位置，已拒绝自动搬移；请先在 Windows 设置中修正该文件夹路径");
        }

        var previousPath = current;
        List<FileFailure> failures = [];
        var moved = 0;

        // 即使选择“不移动文件”，也必须先创建目标目录，避免把 Shell 指向不存在的位置。
        Directory.CreateDirectory(target);
        // 首次迁移的原位置必须保留，连续换盘不能把中间位置覆盖为“原位置”。
        // 在搬文件前先安全持久化，状态写入失败时不会产生文件已移动但无法恢复的半成品。
        var stateAdded = SaveInitialState(def.Key, previousPath);
        try
        {
            if (moveFiles && Directory.Exists(current))
            {
                (moved, failures) = await MigrateDirectoryContentsAsync(current, target, progress, ct).ConfigureAwait(false);
            }

            // 文件步骤结束后再改注册表（见类注释的顺序保证）。
            ApplyRegistryPath(def, target);
        }
        catch
        {
            if (stateAdded)
            {
                ClearState(def.Key);
            }

            throw;
        }

        KnownFolders.NotifyFolderChanged(previousPath, target);

        return new MigrationResult(previousPath, target, moved, moveFiles, failures);
    }

    /// <summary>还原到迁移前的路径；无记录时还原系统默认位置（写 %USERPROFILE% 形式的标准默认值）。</summary>
    public void Restore(KnownFolderDef def)
    {
        var restoreTo = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ReadState(def.Key) ?? def.DefaultPath));
        Directory.CreateDirectory(restoreTo);

        var current = GetCurrentPath(def);
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
        if (!Directory.Exists(source))
        {
            return (0, []);
        }

        var sourceFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        var targetFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        if (string.Equals(sourceFull, targetFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("源目录与目标目录不能相同");
        }

        Directory.CreateDirectory(target);
        var sameVolume = string.Equals(
            Path.GetPathRoot(sourceFull),
            Path.GetPathRoot(targetFull),
            StringComparison.OrdinalIgnoreCase);

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
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
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
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
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
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

    /// <summary>
    /// 防止损坏的已知文件夹配置把磁盘根目录、用户配置根目录或系统目录当作普通内容整体搬走。
    /// 这些位置的任一祖先目录同样视为危险；正常的 Desktop/Documents 等子目录不受影响。
    /// </summary>
    internal static bool IsProtectedMigrationSource(string path)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(source) ?? source);
        if (string.Equals(source, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var protectedPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.SystemDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
        };

        var sourcePrefix = source + Path.DirectorySeparatorChar;
        return protectedPaths
            .Where(protectedPath => !string.IsNullOrWhiteSpace(protectedPath))
            .Select(protectedPath => Path.TrimEndingDirectorySeparator(Path.GetFullPath(protectedPath)))
            .Any(protectedPath =>
                string.Equals(source, protectedPath, StringComparison.OrdinalIgnoreCase)
                || protectedPath.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 用 Windows 的已知文件夹 API 完成重定向，再同步现代/旧版注册表值。
    /// 只写 User Shell Folders 会令属性页和资源管理器导航窗格在部分 Windows 版本中
    /// 使用不同的缓存或兼容值，从而打开旧位置。
    /// </summary>
    private static void ApplyRegistryPath(KnownFolderDef def, string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        KnownFolders.SetKnownFolderPath(def.Id, fullPath);

        using var userShellFolders = Registry.CurrentUser.OpenSubKey(UserShellFoldersKey, writable: true)
            ?? throw new InvalidOperationException("无法打开注册表 User Shell Folders");
        using var shellFolders = Registry.CurrentUser.OpenSubKey(ShellFoldersKey, writable: true)
            ?? throw new InvalidOperationException("无法打开注册表 Shell Folders");

        foreach (var valueName in GetRegistryValueNames(def))
        {
            // User Shell Folders 保存可展开路径，Shell Folders 保存 Explorer 当前解析路径。
            userShellFolders.SetValue(valueName, fullPath, RegistryValueKind.ExpandString);
            shellFolders.SetValue(valueName, fullPath, RegistryValueKind.String);
        }
    }

    internal static IReadOnlyList<string> GetRegistryValueNames(KnownFolderDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        return new[] { def.PrimaryValueName, def.FallbackValueName }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
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
        lock (StateLock)
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
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"默认路径迁移记录损坏，已停止修改以保护原路径：{StateFile}", ex);
            }
        }
    }

    private static string? ReadState(string key)
    {
        return ReadStateMap().TryGetValue(key, out var path) ? path : null;
    }

    /// <summary>由本工具迁移过且尚有还原记录的文件夹数（供开荒进度汇总；只读状态文件，不做目录统计）。</summary>
    public static int CountMigratedFolders()
    {
        var map = ReadStateMap();
        return Folders.Count(def => map.ContainsKey(def.Key));
    }

    /// <returns>本次是否新增了初始状态。</returns>
    private static bool SaveInitialState(string key, string previousPath)
    {
        lock (StateLock)
        {
            Directory.CreateDirectory(StateDirectory);
            var map = ReadStateMap();
            if (!map.TryAdd(key, previousPath))
            {
                return false;
            }

            WriteStateMap(map);
            return true;
        }
    }

    private static void ClearState(string key)
    {
        lock (StateLock)
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
                    WriteStateMap(map);
                }
            }
            catch
            {
                // 状态文件清理失败不影响还原结果
            }
        }
    }

    private static void WriteStateMap(Dictionary<string, string> map)
    {
        Directory.CreateDirectory(StateDirectory);
        var temp = Path.Combine(StateDirectory, $"known-folders-migration.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new MigrationStateMap { PreviousPaths = map }, StateJsonOptions));
            File.Move(temp, StateFile, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // 原子替换成功后通常已无临时文件；失败清理由下次写入覆盖。
            }
        }
    }
}
