using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dnkhw.Core.Downloads;
using Dnkhw.Core.Formatting;

namespace Dnkhw.ViewModels;

public sealed record DriveOption(string Root, string Label, string FreeText, long FreeBytes, string DisplayText);

public sealed record BrowserView(string Name, string StatusText, string SettingsUrl, string? ExePath, bool CanOpen);

public sealed partial class DownloadsViewModel : ObservableObject
{
    private readonly DownloadsMigrationService _service = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private DownloadsStatus? _status;

    [ObservableProperty]
    private IReadOnlyList<DriveOption> _drives = [];

    [ObservableProperty]
    private DriveOption? _selectedDrive;

    [ObservableProperty]
    private bool _moveFiles = true;

    [ObservableProperty]
    private bool _noticeOpen;

    [ObservableProperty]
    private string? _noticeText;

    [ObservableProperty]
    private int _progressCompleted;

    [ObservableProperty]
    private int _progressTotal;

    [ObservableProperty]
    private string? _progressFile;

    [ObservableProperty]
    private IReadOnlyList<string> _lastFailures = [];

    [ObservableProperty]
    private string _redirectStatusText = string.Empty;

    [ObservableProperty]
    private string _sourceFileSummary = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await LoadInternalAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadInternalAsync()
    {
        var status = await Task.Run(() => _service.GetStatus());
        Status = status;

        var currentRoot = Path.GetPathRoot(status.CurrentPath.TrimEnd(Path.DirectorySeparatorChar)) ?? @"C:\";
        var drives = status.Drives
            .Where(d => !string.Equals(d.Root, currentRoot, StringComparison.OrdinalIgnoreCase))
            .Select(d =>
            {
                var free = SizeFormatter.Format(d.FreeBytes);
                return new DriveOption(d.Root, d.Label, free, d.FreeBytes, $"{d.Root} {d.Label}（剩余 {free}）");
            })
            .ToList();
        Drives = drives;
        SelectedDrive = drives.FirstOrDefault(d => d.FreeBytes > status.SourceTotalBytes + 1024 * 1024 * 1024) ?? drives.FirstOrDefault();

        Browsers = status.Browsers.Select(MapBrowser).ToArray();

        RedirectStatusText = status.IsRedirected
            ? "已重定向到其他位置"
            : "系统默认位置（%USERPROFILE%\\Downloads）";
        SourceFileSummary = $"现有内容：{status.SourceFileCount} 个文件，共 {SizeFormatter.Format(status.SourceTotalBytes)}";
    }

    private static BrowserView MapBrowser(BrowserDownloadInfo b)
    {
        if (!b.IsInstalled)
        {
            return new BrowserView(b.Name, "未安装", b.SettingsUrl, null, CanOpen: false);
        }

        return b.UsesSystemDefault
            ? new BrowserView(b.Name, "跟随系统「下载」文件夹，迁移后自动生效", b.SettingsUrl, b.ExecutablePath, CanOpen: false)
            : new BrowserView(b.Name, $"已自定义下载目录：{b.CustomDir}", b.SettingsUrl, b.ExecutablePath, CanOpen: b.ExecutablePath is not null);
    }

    [ObservableProperty]
    private IReadOnlyList<BrowserView> _browsers = [];

    [RelayCommand]
    private async Task MigrateAsync()
    {
        if (IsBusy || SelectedDrive is null || Status is null)
        {
            return;
        }

        var target = Path.Combine(SelectedDrive.Root, "Downloads");
        var moveText = MoveFiles
            ? $"并移动现有 {Status.SourceFileCount} 个文件（{SizeFormatter.Format(Status.SourceTotalBytes)}）。"
            : "不移动现有文件。";
        var message = $"将把下载文件夹从\n{Status.CurrentPath}\n重定向到\n{target}\n\n{moveText}\n\n是否继续？";
        if (System.Windows.MessageBox.Show(message, "确认迁移", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question)
            != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        IsBusy = true;
        NoticeOpen = false;
        ProgressCompleted = 0;
        ProgressTotal = 0;
        ProgressFile = null;
        try
        {
            var progress = new Progress<MigrationProgress>(p =>
            {
                ProgressCompleted = p.Completed;
                ProgressTotal = p.Total;
                ProgressFile = p.CurrentFile;
            });

            var result = await Task.Run(() => _service.MigrateAsync(SelectedDrive.Root, MoveFiles, progress));
            var failureNote = result.Failures.Count > 0 ? $"，{result.Failures.Count} 个失败" : string.Empty;
            NoticeText = $"迁移完成：下载文件夹已指向 {result.NewPath}，移动 {result.MovedCount} 个文件{failureNote}";
            NoticeOpen = true;
            LastFailures = result.Failures.Take(20).Select(f => $"{f.RelativePath}：{f.Reason}").ToArray();
            await LoadInternalAsync();
        }
        catch (Exception ex)
        {
            NoticeText = $"迁移失败：{ex.Message}";
            NoticeOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (System.Windows.MessageBox.Show("将把下载文件夹还原为迁移前的位置，是否继续？", "确认还原", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question)
            != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        IsBusy = true;
        NoticeOpen = false;
        try
        {
            await Task.Run(() => _service.Restore());
            NoticeText = "已还原下载文件夹位置（文件不回移，如需可手动搬回）";
            NoticeOpen = true;
            await LoadInternalAsync();
        }
        catch (Exception ex)
        {
            NoticeText = $"还原失败：{ex.Message}";
            NoticeOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenBrowserSettings(BrowserView? browser)
    {
        if (browser?.ExePath is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(browser.ExePath, browser.SettingsUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            NoticeText = $"无法打开 {browser.Name} 设置：{ex.Message}";
            NoticeOpen = true;
        }
    }
}
