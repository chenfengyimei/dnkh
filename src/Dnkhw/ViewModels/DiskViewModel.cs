using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dnkhw.Core.DiskScan;
using Dnkhw.Core.Formatting;
using Dnkhw.Services;

namespace Dnkhw.ViewModels;

public sealed record DiskDriveView(string Root, string Label, string FreeText, string TotalText);

public sealed record FolderRow(string Name, string Path, string SizeText, int Percent, long FileCount);

public sealed record FileRow(string Name, string Path, string SizeText);

public sealed record JunkRow(string Name, string Path, string SizeText);

public sealed partial class DiskViewModel : ObservableObject
{
    private readonly DiskScanner _scanner = new();
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private CancellationTokenSource? _scanCts;
    private int _scanRequestVersion;

    [ObservableProperty]
    private IReadOnlyList<DiskDriveView> _drives = [];

    [ObservableProperty]
    private DiskDriveView? _selectedDrive;

    [ObservableProperty]
    private bool _isScanning;

    /// <summary>扫描进行中的状态行（如「正在扫描 D:\ · 已扫描 12,345 个文件…」）。</summary>
    [ObservableProperty]
    private string? _progressText;

    /// <summary>扫描完成的摘要（含盘符）。</summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>当前结果所属盘符根（如 C:\），结果卡标题会标注。</summary>
    [ObservableProperty]
    private string? _scannedRoot;

    [ObservableProperty]
    private IReadOnlyList<FolderRow> _topFolders = [];

    [ObservableProperty]
    private IReadOnlyList<FileRow> _topFiles = [];

    [ObservableProperty]
    private IReadOnlyList<JunkRow> _junkLocations = [];

    [ObservableProperty]
    private bool _noticeOpen;

    [ObservableProperty]
    private string? _noticeText;

    [RelayCommand]
    private void Load()
    {
        if (IsScanning)
        {
            return;
        }

        try
        {
            Drives = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d =>
                {
                    var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "本地磁盘" : d.VolumeLabel;
                    return new DiskDriveView(
                        d.RootDirectory.FullName,
                        label,
                        $"剩余 {SizeFormatter.Format(d.AvailableFreeSpace)}",
                        $"总 {SizeFormatter.Format(d.TotalSize)}");
                })
                .ToList();

            // 首次选中即自动扫描（默认系统盘），切换盘符同样自动重扫
            SelectedDrive ??= Drives.FirstOrDefault(d => d.Root.StartsWith("C", StringComparison.OrdinalIgnoreCase))
                ?? Drives.FirstOrDefault();
            SafeFireAndForget.Run("垃圾位置统计", () => LoadJunkAsync());
        }
        catch (Exception ex)
        {
            LogService.Error("磁盘分析：读取磁盘列表失败", ex);
            NoticeText = $"读取磁盘列表失败：{ex.Message}";
            NoticeOpen = true;
        }
    }

    /// <summary>切换盘符即重新扫描；若旧盘仍在扫描则先取消，新扫描串行接续。</summary>
    partial void OnSelectedDriveChanged(DiskDriveView? oldValue, DiskDriveView? newValue)
    {
        if (newValue is null || Equals(oldValue, newValue))
        {
            return;
        }

        if (IsScanning)
        {
            _scanCts?.Cancel();
        }

        // 清掉旧盘结果并给出即时反馈，避免「不确定有没有切换成功」
        TopFolders = [];
        TopFiles = [];
        ScannedRoot = null;
        SummaryText = string.Empty;
        ProgressText = $"正在扫描 {newValue.Root} …";
        var requestVersion = Interlocked.Increment(ref _scanRequestVersion);
        SafeFireAndForget.Run("磁盘扫描", () => QueueScanAsync(requestVersion));
    }

    private async Task LoadJunkAsync()
    {
        try
        {
            var junk = await _scanner.GetJunkLocationsAsync();
            JunkLocations = junk.Select(j => new JunkRow(j.Name, j.Path, j.Bytes > 0 ? SizeFormatter.Format(j.Bytes) : "—")).ToList();
        }
        catch (Exception ex)
        {
            // 垃圾位置统计失败不影响主流程
            LogService.Warn("垃圾位置统计失败", ex);
        }
    }

    [RelayCommand]
    private Task ScanAsync()
    {
        _scanCts?.Cancel();
        return QueueScanAsync(Interlocked.Increment(ref _scanRequestVersion));
    }

    private async Task QueueScanAsync(int requestVersion)
    {
        var requestedRoot = SelectedDrive?.Root ?? "(未选择)";
        await _scanLock.WaitAsync();
        try
        {
            // 多次快速切盘时丢弃已经过期的排队请求，只扫描最后一次选择。
            if (requestVersion != Volatile.Read(ref _scanRequestVersion))
            {
                return;
            }

            var drive = SelectedDrive;
            if (drive is null)
            {
                return;
            }

            IsScanning = true;
            NoticeOpen = false;
            ProgressText = $"正在扫描 {drive.Root} …";
            TopFolders = [];
            TopFiles = [];
            SummaryText = string.Empty;
            using var scanCts = new CancellationTokenSource();
            _scanCts = scanCts;
            try
            {
                var progress = new Progress<long>(scanned =>
                {
                    if (requestVersion == Volatile.Read(ref _scanRequestVersion))
                    {
                        ProgressText = $"正在扫描 {drive.Root} · 已扫描 {scanned:N0} 个文件…";
                    }
                });
                var result = await _scanner.ScanAsync(drive.Root, progress, scanCts.Token);

                if (requestVersion != Volatile.Read(ref _scanRequestVersion))
                {
                    return;
                }

                ScannedRoot = result.Root;
                SummaryText = $"共 {result.ScannedFiles:N0} 个文件，统计占用 {SizeFormatter.Format(result.ScannedBytes)}"
                    + (result.SkippedEntries > 0 ? $"（{result.SkippedEntries:N0} 项无权限跳过）" : string.Empty);
                ProgressText = null;

                TopFolders = result.TopFolders.Select(f => new FolderRow(
                    Path.GetFileName(f.Path.TrimEnd(Path.DirectorySeparatorChar)),
                    f.Path,
                    SizeFormatter.Format(f.Bytes),
                    f.Percent,
                    f.FileCount)).ToList();

                TopFiles = result.TopFiles.Select(f => new FileRow(
                    Path.GetFileName(f.Path),
                    f.Path,
                    SizeFormatter.Format(f.Bytes))).ToList();
            }
            finally
            {
                if (ReferenceEquals(_scanCts, scanCts))
                {
                    _scanCts = null;
                }
            }
        }
        catch (OperationCanceledException)
        {
            LogService.Info($"磁盘分析：扫描已取消；根目录={requestedRoot}");
            if (requestVersion == Volatile.Read(ref _scanRequestVersion))
            {
                ProgressText = null;
                NoticeText = "扫描已取消，可换盘或点「重新扫描」";
                NoticeOpen = true;
            }
        }
        catch (Exception ex)
        {
            LogService.Error($"磁盘分析：扫描失败；根目录={requestedRoot}", ex);
            if (requestVersion == Volatile.Read(ref _scanRequestVersion))
            {
                ProgressText = null;
                NoticeText = $"扫描失败：{ex.Message}";
                NoticeOpen = true;
            }
        }
        finally
        {
            if (requestVersion == Volatile.Read(ref _scanRequestVersion))
            {
                IsScanning = false;
            }

            _scanLock.Release();
        }
    }

    [RelayCommand]
    private void CancelScan() => _scanCts?.Cancel();

    [RelayCommand]
    private void OpenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            // 文件在资源管理器中定位选中，目录直接打开
            var isFile = File.Exists(path);
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            var startInfo = new ProcessStartInfo(explorer)
            {
                UseShellExecute = false,
            };
            if (isFile)
            {
                startInfo.ArgumentList.Add("/select,");
            }

            startInfo.ArgumentList.Add(path);
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            LogService.Warn($"磁盘分析：无法打开位置；路径={path}", ex);
            NoticeText = $"无法打开位置：{ex.Message}";
            NoticeOpen = true;
        }
    }
}
