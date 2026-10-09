using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dnkhw.Core.Downloads;
using Dnkhw.Core.Formatting;
using Microsoft.Win32;

namespace Dnkhw.ViewModels;

/// <summary>文件夹选择 chip（仅快速信息：名称/当前路径/重定向状态，不统计文件——统计只在选中后进行）。</summary>
public sealed record FolderView(string Key, string Name, string CurrentPath, bool IsRedirected, KnownFolderDef Def);

/// <summary>浏览器下载设置状态行（仅下载文件夹相关）。</summary>
public sealed record BrowserView(string Name, string StatusText, string SettingsUrl, string? ExePath, bool CanOpen);

/// <summary>目标盘选项。</summary>
public sealed record DriveOption(string Root, string Label, string FreeText, string DisplayText);

public sealed partial class DefaultPathsViewModel : ObservableObject
{
    private readonly KnownFoldersMigrationService _service = new();

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>选中的文件夹（与 chip 列表中的项为同一引用，ListBox 选中态由此驱动）。</summary>
    [ObservableProperty]
    private FolderView? _selectedFolder;

    [ObservableProperty]
    private IReadOnlyList<FolderView> _folders = [];

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
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private bool _isRedirected;

    [ObservableProperty]
    private bool _isOneDriveManaged;

    [ObservableProperty]
    private bool _canRestore;

    [ObservableProperty]
    private string _redirectStatusText = string.Empty;

    [ObservableProperty]
    private string _sourceFileSummary = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<BrowserView> _browsers = [];

    [ObservableProperty]
    private IReadOnlyList<DriveOption> _drives = [];

    public bool IsDownloadsSelected => SelectedFolder?.Key == "downloads";

    private bool _suppressSelectionLoad;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        NoticeOpen = false;
        try
        {
            // 六个文件夹的路径/重定向状态读取是瞬时的，chip 立即出现；文件统计在选中后进行
            Folders = KnownFoldersMigrationService.Folders
                .Select(def =>
                {
                    var current = KnownFolders.GetKnownFolderPath(def.Id);
                    var isRedirected = !string.Equals(current, def.DefaultPath, StringComparison.OrdinalIgnoreCase);
                    return new FolderView(def.Key, def.Name, current, isRedirected, def);
                })
                .ToList();

            _suppressSelectionLoad = true;
            SelectedFolder ??= Folders.FirstOrDefault(f => f.Key == "downloads");
            _suppressSelectionLoad = false;

            // 首次详情同步等待，保持加载态直到统计完成
            if (SelectedFolder is not null)
            {
                await LoadDetailAsync(SelectedFolder);
            }
        }
        catch (Exception ex)
        {
            NoticeText = $"读取状态失败：{ex.Message}";
            NoticeOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>选中文件夹即加载详情（内容统计可能较慢，后台进行并显示加载态）。</summary>
    partial void OnSelectedFolderChanged(FolderView? oldValue, FolderView? newValue)
    {
        if (_suppressSelectionLoad || newValue is null || Equals(oldValue, newValue))
        {
            return;
        }

        _ = LoadDetailAsync(newValue);
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

    private async Task LoadDetailAsync(FolderView folder)
    {
        var def = folder.Def;
        IsBusy = true;
        NoticeOpen = false;
        try
        {
            var status = await Task.Run(() => _service.GetStatus(def));

            CurrentPath = status.CurrentPath;
            IsRedirected = status.IsRedirected;
            IsOneDriveManaged = status.IsManagedByOneDrive;
            CanRestore = status.SavedPreviousPath is not null;
            RedirectStatusText = status.IsManagedByOneDrive
                ? "该文件夹当前由 OneDrive 接管，迁移前建议先在 OneDrive 设置中关闭文件夹备份"
                : status.IsRedirected
                    ? "已重定向到其他位置"
                    : "系统默认位置";
            SourceFileSummary = $"现有内容：{status.FileCount:N0} 个文件，共 {SizeFormatter.Format(status.TotalBytes)}";

            var currentRoot = Path.GetPathRoot(status.CurrentPath.TrimEnd(Path.DirectorySeparatorChar)) ?? @"C:\";
            Drives = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Where(d => !string.Equals(d.RootDirectory.FullName, currentRoot, StringComparison.OrdinalIgnoreCase))
                .Select(d =>
                {
                    var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "本地磁盘" : d.VolumeLabel;
                    var free = SizeFormatter.Format(d.AvailableFreeSpace);
                    return new DriveOption(d.RootDirectory.FullName, label, free, $"{d.RootDirectory.FullName} {label}（剩余 {free}）");
                })
                .ToList();
            SelectedDrive = Drives.FirstOrDefault();

            Browsers = def.Key == "downloads"
                ? new BrowserDownloadDetector().Detect().Select(MapBrowser).ToList()
                : [];

            OnPropertyChanged(nameof(IsDownloadsSelected));
        }
        catch (Exception ex)
        {
            NoticeText = $"读取「{folder.Name}」状态失败：{ex.Message}";
            NoticeOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task MigrateAsync()
    {
        var folder = SelectedFolder;
        if (IsBusy || folder is null || SelectedDrive is null)
        {
            return;
        }

        var target = Path.Combine(SelectedDrive.Root, folder.Def.DefaultSubFolder);
        var moveText = MoveFiles
            ? $"并移动现有内容（{SourceFileSummary.Replace("现有内容：", string.Empty)}）。"
            : "不移动现有文件。";
        var message = $"将把「{folder.Name}」文件夹从\n{CurrentPath}\n重定向到\n{target}\n\n{moveText}\n\n是否继续？";
        if (MessageBox.Show(message, "确认迁移", MessageBoxButton.OKCancel, MessageBoxImage.Question)
            != MessageBoxResult.OK)
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

            var result = await Task.Run(() => _service.MigrateAsync(folder.Def, SelectedDrive.Root, MoveFiles, progress));
            var failureNote = result.Failures.Count > 0 ? $"，{result.Failures.Count} 个失败" : string.Empty;
            NoticeText = $"迁移完成：「{folder.Name}」已指向 {result.NewPath}，移动 {result.MovedCount} 个文件{failureNote}";
            NoticeOpen = true;
            LastFailures = result.Failures.Take(20).Select(f => $"{f.RelativePath}：{f.Reason}").ToList();
            await LoadDetailAsync(folder);
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
        var folder = SelectedFolder;
        if (IsBusy || folder is null)
        {
            return;
        }

        if (MessageBox.Show($"将把「{folder.Name}」文件夹还原为迁移前的位置，是否继续？", "确认还原",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        IsBusy = true;
        NoticeOpen = false;
        try
        {
            await Task.Run(() => _service.Restore(folder.Def));
            NoticeText = $"「{folder.Name}」已还原（文件不回移，如需可手动搬回）";
            NoticeOpen = true;
            await LoadDetailAsync(folder);
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
