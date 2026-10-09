using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Dnkhw.Core.Downloads;

/// <summary>
/// 下载路径换盘：状态采集、文件迁移与已知文件夹重定向。
/// 顺序保证：先完成文件迁移，再写注册表并通知 Explorer——中断时不会出现「指针已改文件未动」的状态。
/// </summary>
public sealed class DownloadsMigrationService
{
    private const string UserShellFoldersKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";

    private static readonly JsonSerializerOptions StateJsonOptions = new()
    {
        WriteIndented = true,
    };

    private static string StateDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dnkhw");

    private static string StateFile => Path.Combine(StateDirectory, "downloads-migration.json");

    /// <summary>当前下载文件夹的默认位置（未被重定向时即等于它）。</summary>
    public static string GetDefaultPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public DownloadsStatus GetStatus()
    {
        var current = KnownFolders.GetDownloadsPath();
        var defaultPath = GetDefaultPath();
        var isRedirected = !string.Equals(current, defaultPath, StringComparison.OrdinalIgnoreCase);

        (long count, long bytes) = CountFiles(current);

        var drives = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new DownloadDrive(
                d.RootDirectory.FullName,
                string.IsNullOrWhiteSpace(d.VolumeLabel) ? "本地磁盘" : d.VolumeLabel,
                d.AvailableFreeSpace))
            .ToList();

        var browsers = new BrowserDownloadDetector().Detect();

        return new DownloadsStatus(
            current,
            defaultPath,
            isRedirected,
            count,
            bytes,
            drives,
            browsers,
            ReadState()?.PreviousPath);
    }

    /// <summary>
    /// 迁移到目标盘的 Downloads 目录。仅当 moveFiles 为 true 时搬动文件；无论是否搬动文件都会重定向注册表。
    /// </summary>
    public async Task<MigrationResult> MigrateAsync(string targetRoot, bool moveFiles, IProgress<MigrationProgress>? progress, CancellationToken ct = default)
    {
        var current = KnownFolders.GetDownloadsPath();
        var target = Path.Combine(targetRoot, "Downloads");

        if (string.Equals(Path.GetPathRoot(current.TrimEnd(Path.DirectorySeparatorChar)), Path.GetPathRoot(targetRoot), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标盘与当前下载文件夹在同一个分区，无需迁移");
        }

        var previousPath = current;
        List<FileFailure> failures = [];
        var moved = 0;

        if (moveFiles)
        {
            Directory.CreateDirectory(target);
            (moved, failures) = await MigrateDirectoryContentsAsync(current, target, progress, ct).ConfigureAwait(false);
        }

        // 文件步骤结束后再改注册表（见类型注释的顺序保证）
        ApplyRegistryPath(target);
        SaveState(previousPath);
        KnownFolders.NotifyFolderChanged(previousPath, target);

        return new MigrationResult(previousPath, target, moved, moveFiles, failures);
    }

    /// <summary>还原到迁移前的路径；无记录时还原系统默认（%USERPROFILE%\Downloads）。</summary>
    public void Restore()
    {
        var restoreTo = ReadState()?.PreviousPath ?? GetDefaultPath();
        Directory.CreateDirectory(restoreTo);

        var current = KnownFolders.GetDownloadsPath();
        ApplyRegistryPath(restoreTo);
        ClearState();
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

    private static void ApplyRegistryPath(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(UserShellFoldersKey, writable: true)
            ?? throw new InvalidOperationException("无法打开注册表 User Shell Folders");
        key.SetValue($"{{{KnownFolders.DownloadsId}}}", path, RegistryValueKind.ExpandString);
    }

    private sealed class MigrationState
    {
        [JsonPropertyName("previousPath")]
        public string PreviousPath { get; set; } = string.Empty;

        [JsonPropertyName("migratedAtUtc")]
        public DateTime MigratedAtUtc { get; set; } = DateTime.UtcNow;
    }

    private static MigrationState? ReadState()
    {
        try
        {
            if (!File.Exists(StateFile))
            {
                return null;
            }

            return JsonSerializer.Deserialize<MigrationState>(File.ReadAllText(StateFile));
        }
        catch
        {
            return null;
        }
    }

    private static void SaveState(string previousPath)
    {
        Directory.CreateDirectory(StateDirectory);
        File.WriteAllText(StateFile, JsonSerializer.Serialize(new MigrationState { PreviousPath = previousPath }, StateJsonOptions));
    }

    private static void ClearState()
    {
        try
        {
            if (File.Exists(StateFile))
            {
                File.Delete(StateFile);
            }
        }
        catch
        {
            // 状态文件清理失败不影响还原结果
        }
    }
}
