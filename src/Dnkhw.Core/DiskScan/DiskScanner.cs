namespace Dnkhw.Core.DiskScan;

/// <summary>一级目录占用统计（含全部子级）。</summary>
public sealed record FolderUsage(string Path, long Bytes, int Percent, long FileCount);

/// <summary>文件占用统计。</summary>
public sealed record FileUsage(string Path, long Bytes);

public sealed record DiskScanResult(
    string Root,
    long ScannedBytes,
    long RootLooseBytes,
    IReadOnlyList<FolderUsage> TopFolders,
    IReadOnlyList<FileUsage> TopFiles,
    long ScannedFiles,
    long SkippedEntries);

public sealed record JunkLocation(string Name, string Path, long Bytes);

/// <summary>
/// 磁盘空间分析：递归聚合一级目录/文件占用（只读，不修改任何文件）。
/// </summary>
public sealed class DiskScanner
{
    private const int ReportBatch = 200;
    private const int TopCount = 30;

    public async Task<DiskScanResult> ScanAsync(
        string root,
        IProgress<long>? progress,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"目录不存在：{root}");
        }

        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var folders = new Dictionary<string, (long Bytes, long Files)>(StringComparer.OrdinalIgnoreCase);
        var files = new List<FileUsage>();
        long totalBytes = 0;
        long rootLoose = 0;
        long scanned = 0;
        long skipped = 0;

        await Task.Run(() =>
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                // 仅跳过重解析点避免符号链接环；隐藏/系统文件正常计入
                AttributesToSkip = FileAttributes.ReparsePoint,
            };

            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                long size;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch
                {
                    skipped++;
                    continue;
                }

                scanned++;
                totalBytes += size;
                files.Add(new FileUsage(file, size));

                // 归属到根下的一级目录（含其全部子级）
                var relative = Path.GetRelativePath(rootFull, file);
                var separator = relative.IndexOfAny(['\\', '/']);
                if (separator < 0)
                {
                    rootLoose += size; // 根目录下的散文件
                }
                else
                {
                    var topFolder = Path.Combine(rootFull, relative[..separator]);
                    var current = folders.TryGetValue(topFolder, out var v) ? v : (0, 0);
                    folders[topFolder] = (current.Bytes + size, current.Files + 1);
                }

                if (scanned % ReportBatch == 0)
                {
                    progress?.Report(scanned);
                }
            }

            progress?.Report(scanned);
        }, ct).ConfigureAwait(false);

        var topFolders = folders
            .OrderByDescending(kv => kv.Value.Bytes)
            .Take(TopCount)
            .Select(kv => new FolderUsage(
                kv.Key,
                kv.Value.Bytes,
                totalBytes == 0 ? 0 : (int)Math.Round(kv.Value.Bytes * 100.0 / totalBytes),
                kv.Value.Files))
            .ToList();

        var topFiles = files
            .OrderByDescending(f => f.Bytes)
            .Take(TopCount)
            .ToList();

        return new DiskScanResult(root, totalBytes, rootLoose, topFolders, topFiles, scanned, skipped);
    }

    /// <summary>
    /// 常见「开荒垃圾」位置占用（只展示，不删除）。
    /// </summary>
    public async Task<IReadOnlyList<JunkLocation>> GetJunkLocationsAsync(CancellationToken ct = default)
    {
        var sysRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        (string Name, string Path)[] definitions =
        [
            ("回收站", Path.Combine(sysRoot, "$Recycle.Bin")),
            ("系统 Temp", Path.Combine(windows, "Temp")),
            ("用户 Temp", Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)),
            ("Windows.old（旧系统备份）", Path.Combine(sysRoot, "Windows.old")),
            ("更新缓存（SoftwareDistribution\\Download）", Path.Combine(sysRoot, @"Windows\SoftwareDistribution\Download")),
            ("Delivery Optimization 文件", Path.Combine(sysRoot, @"Windows\SoftwareDistribution\DeliveryOptimization")),
            ("休眠文件 hiberfil.sys", Path.Combine(sysRoot, "hiberfil.sys")),
        ];

        return await Task.Run(
            () => definitions.Select(d => new JunkLocation(d.Name, d.Path, TryGetSize(d.Path))).ToList(),
            ct).ConfigureAwait(false);
    }

    private static long TryGetSize(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }

            if (Directory.Exists(path))
            {
                long total = 0;
                var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in Directory.EnumerateFiles(path, "*", options))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // 单文件统计失败忽略
                    }
                }

                return total;
            }
        }
        catch
        {
            // 整体不可访问返回 0
        }

        return 0;
    }
}
