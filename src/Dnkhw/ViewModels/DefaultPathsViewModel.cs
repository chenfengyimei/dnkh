using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dnkhw.Core.Downloads;
using Dnkhw.Core.Formatting;
using Dnkhw.Services;

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
    private int _detailLoadVersion;

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
            // chip 与详情使用同一个注册表真相源，避免 SHGetKnownFolderPath 失败时显示旧路径。
            RefreshFolders(SelectedFolder?.Key ?? "downloads");

            // 首次详情同步等待，保持加载态直到统计完成
            if (SelectedFolder is not null)
            {
                await LoadDetailAsync(SelectedFolder, clearNotice: false);
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

        OnPropertyChanged(nameof(IsDownloadsSelected));
        SafeFireAndForget.Run("默认路径详情加载", () => LoadDetailAsync(newValue));
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

    private async Task LoadDetailAsync(FolderView folder, bool clearNotice = true)
    {
        var loadVersion = Interlocked.Increment(ref _detailLoadVersion);
        var def = folder.Def;
        IsBusy = true;
        if (clearNotice)
        {
            NoticeOpen = false;
        }

        try
        {
            var status = await Task.Run(() => _service.GetStatus(def));
            if (loadVersion != Volatile.Read(ref _detailLoadVersion)
                || SelectedFolder?.Key != folder.Key)
            {
                return;
            }

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
            if (loadVersion == Volatile.Read(ref _detailLoadVersion))
            {
                NoticeText = $"读取「{folder.Name}」状态失败：{ex.Message}";
                NoticeOpen = true;
            }
        }
        finally
        {
            if (loadVersion == Volatile.Read(ref _detailLoadVersion))
            {
                IsBusy = false;
            }
        }
    }

    private void RefreshFolders(string preferredKey)
    {
        var folders = KnownFoldersMigrationService.Folders
            .Select(def =>
            {
                var current = _service.GetCurrentPath(def);
                var defaultPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(def.DefaultPath));
                return new FolderView(
                    def.Key,
                    def.Name,
                    current,
                    !string.Equals(current, defaultPath, StringComparison.OrdinalIgnoreCase),
                    def);
            })
            .ToList();

        _suppressSelectionLoad = true;
        try
        {
            Folders = folders;
            SelectedFolder = folders.FirstOrDefault(f => f.Key == preferredKey) ?? folders.FirstOrDefault();
        }
        finally
        {
            _suppressSelectionLoad = false;
        }

        OnPropertyChanged(nameof(IsDownloadsSelected));
    }

    [RelayCommand]
    private async Task MigrateAsync()
    {
        var folder = SelectedFolder;
        var selectedDrive = SelectedDrive;
        if (IsBusy || folder is null || selectedDrive is null)
        {
            return;
        }

        var target = Path.Combine(selectedDrive.Root, folder.Def.DefaultSubFolder);
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

            var result = await _service.MigrateAsync(folder.Def, selectedDrive.Root, MoveFiles, progress);
            var failureNote = result.Failures.Count > 0 ? $"，{result.Failures.Count} 个失败" : string.Empty;
            LastFailures = result.Failures.Take(20).Select(f => $"{f.RelativePath}：{f.Reason}").ToList();
            RefreshFolders(folder.Key);
            if (SelectedFolder is not null)
            {
                await LoadDetailAsync(SelectedFolder, clearNotice: false);
            }

            NoticeText = $"迁移完成：「{folder.Name}」已指向 {result.NewPath}，移动 {result.MovedCount} 个文件{failureNote}";
            NoticeOpen = true;
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
            RefreshFolders(folder.Key);
            if (SelectedFolder is not null)
            {
                await LoadDetailAsync(SelectedFolder, clearNotice: false);
            }

            NoticeText = $"「{folder.Name}」已还原（文件不回移，如需可手动搬回）";
            NoticeOpen = true;
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
