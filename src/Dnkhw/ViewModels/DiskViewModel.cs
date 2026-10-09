using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dnkhw.Core.DiskScan;
using Dnkhw.Core.Formatting;

namespace Dnkhw.ViewModels;

public sealed record DiskDriveView(string Root, string Label, string DisplayText);

public sealed record FolderRow(string Name, string Path, string SizeText, int Percent, long FileCount);

public sealed record FileRow(string Name, string Path, string SizeText);

public sealed record JunkRow(string Name, string Path, string SizeText);

public sealed partial class DiskViewModel : ObservableObject
{
    private readonly DiskScanner _scanner = new();
    private CancellationTokenSource? _scanCts;

    [ObservableProperty]
    private IReadOnlyList<DiskDriveView> _drives = [];

    [ObservableProperty]
    private DiskDriveView? _selectedDrive;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string? _progressText;

    [ObservableProperty]
    private string _summaryText = string.Empty;

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
                        $"{d.RootDirectory.FullName} {label}（总 {SizeFormatter.Format(d.TotalSize)}，剩 {SizeFormatter.Format(d.AvailableFreeSpace)}）");
                })
                .ToList();
            SelectedDrive ??= Drives.FirstOrDefault(d => d.Root.StartsWith("C", StringComparison.OrdinalIgnoreCase)) ?? Drives.FirstOrDefault();
            _ = LoadJunkAsync();
        }
        catch (Exception ex)
        {
            NoticeText = $"读取磁盘列表失败：{ex.Message}";
            NoticeOpen = true;
        }
    }

    private async Task LoadJunkAsync()
    {
        try
        {
            var junk = await _scanner.GetJunkLocationsAsync();
            JunkLocations = junk.Select(j => new JunkRow(j.Name, j.Path, j.Bytes > 0 ? SizeFormatter.Format(j.Bytes) : "—")).ToList();
        }
        catch
        {
            // 垃圾位置统计失败不影响主流程
        }
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning || SelectedDrive is null)
        {
            return;
        }

        IsScanning = true;
        NoticeOpen = false;
        ProgressText = "正在扫描…";
        TopFolders = [];
        TopFiles = [];
        _scanCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<long>(scanned => ProgressText = $"已扫描 {scanned:N0} 个文件…");
            var result = await _scanner.ScanAsync(SelectedDrive.Root, progress, _scanCts.Token);

            SummaryText = $"共 {result.ScannedFiles:N0} 个文件，统计占用 {SizeFormatter.Format(result.ScannedBytes)}"
                + (result.SkippedEntries > 0 ? $"（{result.SkippedEntries:N0} 项无权限跳过）" : string.Empty);

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
        catch (OperationCanceledException)
        {
            NoticeText = "扫描已取消";
            NoticeOpen = true;
        }
        catch (Exception ex)
        {
            NoticeText = $"扫描失败：{ex.Message}";
            NoticeOpen = true;
        }
        finally
        {
            IsScanning = false;
            ProgressText = null;
            _scanCts.Dispose();
            _scanCts = null;
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
            var arguments = isFile ? $"/select,\"{path}\"" : path;
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            NoticeText = $"无法打开位置：{ex.Message}";
            NoticeOpen = true;
        }
    }
}
