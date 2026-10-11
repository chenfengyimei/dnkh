using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dnkhw.Core.AppCatalog;
using Dnkhw.Core.DesktopTweaks;
using Dnkhw.Core.Wallpaper;
using Microsoft.Win32;
namespace Dnkhw.ViewModels;

public sealed record FitOption(string Label, WallpaperFit Fit);

public sealed record WallpaperView(
    string Title,
    string DateText,
    string? ThumbPath,
    string? FullPath,
    bool IsDownloaded,
    bool IsLocal,
    BingWallpaper? Source);

public sealed partial class WallpaperViewModel : ObservableObject
{
    private readonly BingWallpaperService _bing = new();
    private readonly WallpaperStore _store = new();
    private readonly WingetInstaller _winget = new();

    private const string TranslucentTbWingetId = "CharlesMilette.TranslucentTB";
    private const string SteamWingetId = "Valve.Steam";

    /// <summary>Wallpaper Engine（壁纸引擎）的 Steam 商店页，steam: 协议缺失时回退 Web 地址。</summary>
    private const string WallpaperEngineStoreUrl = "steam://store/431960";
    private const string WallpaperEngineWebUrl = "https://store.steampowered.com/app/431960/Wallpaper_Engine/";

    public IReadOnlyList<FitOption> FitOptions { get; } =
    [
        new FitOption("填充", WallpaperFit.Fill),
        new FitOption("适应", WallpaperFit.Fit),
        new FitOption("拉伸", WallpaperFit.Stretch),
        new FitOption("平铺", WallpaperFit.Tile),
        new FitOption("居中", WallpaperFit.Center),
    ];

    [ObservableProperty]
    private FitOption? _selectedFit;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private IReadOnlyList<WallpaperView> _items = [];

    [ObservableProperty]
    private bool _noticeOpen;

    [ObservableProperty]
    private string? _noticeText;

    [ObservableProperty]
    private bool _desktopIconsHidden;

    [ObservableProperty]
    private bool _taskbarAutoHide;

    [ObservableProperty]
    private bool _translucentInstalled;

    [ObservableProperty]
    private bool _steamInstalled;

    public WallpaperViewModel()
    {
        _selectedFit = FitOptions[0];
        LoadTweakStates();
    }

    /// <summary>读取桌面美化的当前系统状态（图标隐藏 / 任务栏自动隐藏 / TranslucentTB、Steam 安装情况）。</summary>
    private void LoadTweakStates()
    {
        try
        {
            DesktopIconsHidden = DesktopTweaksService.GetDesktopIconsHidden();
        }
        catch
        {
            // 读取失败保持默认 false
        }

        try
        {
            TaskbarAutoHide = DesktopTweaksService.GetTaskbarAutoHide();
        }
        catch
        {
            // 读取失败保持默认 false
        }

        try
        {
            TranslucentInstalled = InstallDetector.IsInstalled(
                "TranslucentTB",
                InstallDetector.GetInstalledDisplayNames());
        }
        catch
        {
            TranslucentInstalled = false;
        }

        try
        {
            SteamInstalled = InstallDetector.IsInstalled(
                "Steam",
                InstallDetector.GetInstalledDisplayNames());
        }
        catch
        {
            SteamInstalled = false;
        }
    }

    /// <summary>隐藏/显示桌面图标（开关 Click 调用，此刻已是目标值，失败时回拨）。</summary>
    public async Task ToggleIconsAsync()
    {
        var target = DesktopIconsHidden;
        try
        {
            await Task.Run(() => DesktopTweaksService.SetDesktopIconsHidden(target));
            NoticeText = target ? "桌面图标已隐藏（桌面右键「查看」可随时恢复）" : "桌面图标已恢复显示";
            NoticeOpen = true;
        }
        catch (Exception ex)
        {
            DesktopIconsHidden = !target;
            NoticeText = $"操作失败：{ex.Message}";
            NoticeOpen = true;
        }
    }

    /// <summary>任务栏自动隐藏开关（同上模式）。</summary>
    public async Task ToggleTaskbarAsync()
    {
        var target = TaskbarAutoHide;
        try
        {
            await Task.Run(() => DesktopTweaksService.SetTaskbarAutoHide(target));
            NoticeText = target ? "任务栏已自动隐藏（鼠标移至屏幕底部显示）" : "任务栏已恢复常驻显示";
            NoticeOpen = true;
        }
        catch (Exception ex)
        {
            TaskbarAutoHide = !target;
            NoticeText = $"操作失败：{ex.Message}";
            NoticeOpen = true;
        }
    }

    /// <summary>一键安装 TranslucentTB（winget 静默安装）。</summary>
    [RelayCommand]
    private async Task InstallTranslucentAsync()
    {
        if (IsBusy || TranslucentInstalled)
        {
            return;
        }

        IsBusy = true;
        NoticeOpen = false;
        try
        {
            var result = await Task.Run(() => _winget.InstallAsync(TranslucentTbWingetId));
            if (result.Success)
            {
                TranslucentInstalled = true;
                NoticeText = "TranslucentTB 安装完成：托盘图标右键即可打开设置";
                NoticeOpen = true;
            }
            else
            {
                NoticeText = $"安装失败：{FirstMeaningfulLine(result.Output)}（可改用教程手动安装）";
                NoticeOpen = true;
            }
        }
        catch (Exception ex)
        {
            NoticeText = $"安装失败：{ex.Message}（可改用教程手动安装）";
            NoticeOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>打开 Wallpaper Engine（壁纸引擎）商店页：本机有 Steam 则直开客户端，否则回退浏览器 Web 页。</summary>
    [RelayCommand]
    private void OpenWallpaperEngineStore()
    {
        try
        {
            Process.Start(new ProcessStartInfo(WallpaperEngineStoreUrl) { UseShellExecute = true });
            NoticeText = "已打开 Steam 商店页，购买安装壁纸引擎后即可使用动态壁纸";
            NoticeOpen = true;
        }
        catch
        {
            // 未装 Steam（无 steam: 协议处理器）等场景回退浏览器打开
            OpenExternal(WallpaperEngineWebUrl, "Steam 商店页");
        }
    }

    /// <summary>一键安装 Steam（winget 静默安装，供未装 Steam 的用户直达壁纸引擎）。</summary>
    [RelayCommand]
    private async Task InstallSteamAsync()
    {
        if (IsBusy || SteamInstalled)
        {
            return;
        }

        IsBusy = true;
        NoticeOpen = false;
        try
        {
            var result = await Task.Run(() => _winget.InstallAsync(SteamWingetId));
            if (result.Success)
            {
                SteamInstalled = true;
                NoticeText = "Steam 安装完成：登录账号后到商店页购买壁纸引擎即可";
                NoticeOpen = true;
            }
            else
            {
                NoticeText = $"Steam 安装失败：{FirstMeaningfulLine(result.Output)}（可点「打开 Steam 商店页」用浏览器下载安装器）";
                NoticeOpen = true;
            }
        }
        catch (Exception ex)
        {
            NoticeText = $"Steam 安装失败：{ex.Message}（可点「打开 Steam 商店页」用浏览器下载安装器）";
            NoticeOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenRocketDockSite()
    {
        OpenExternal("https://rocketdock.com/", "RocketDock 官网");
    }

    [RelayCommand]
    private void OpenStoreDockSearch()
    {
        OpenExternal("ms-windows-store://search/?query=MyDockFinder", "Microsoft Store");
    }

    private void OpenExternal(string target, string displayName)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            NoticeText = $"无法打开 {displayName}：{ex.Message}";
            NoticeOpen = true;
        }
    }

    private static string FirstMeaningfulLine(string output)
    {
        var line = output
            .Split('\r', '\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('-'));
        return line ?? "未知原因（可重试或改用教程手动安装）";
    }

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
            IReadOnlyList<BingWallpaper> list;
            try
            {
                list = await _bing.GetRecentAsync(8);
            }
            catch (Exception ex)
            {
                list = [];
                NoticeText = $"Bing 接口不可用（{ex.Message}），仅显示本地缓存";
                NoticeOpen = true;
            }

            var items = new List<WallpaperView>();
            var recentFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var w in list)
            {
                string? thumbPath = null;
                try
                {
                    thumbPath = await _store.DownloadThumbAsync(w);
                }
                catch
                {
                    // 缩略图下载失败仅影响预览
                }

                var fullPath = _store.GetFullPath(w.FullFileName);
                recentFiles.Add(Path.GetFileName(fullPath));
                items.Add(new WallpaperView(
                    w.Title,
                    FormatDate(w.EndDate),
                    thumbPath,
                    fullPath,
                    File.Exists(fullPath),
                    IsLocal: false,
                    Source: w));
            }

            // 附加本地缓存中不在最近一周列表内的文件（离线可用）
            foreach (var file in _store.GetCachedFiles())
            {
                if (!recentFiles.Contains(Path.GetFileName(file)))
                {
                    items.Add(new WallpaperView(
                        Path.GetFileNameWithoutExtension(file),
                        string.Empty,
                        file,
                        file,
                        IsDownloaded: true,
                        IsLocal: true,
                        Source: null));
                }
            }

            Items = items;
        }
        catch (Exception ex)
        {
            NoticeText = $"加载壁纸失败：{ex.Message}";
            NoticeOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetWallpaperAsync(WallpaperView? view)
    {
        if (view is null)
        {
            return;
        }

        IsBusy = true;
        NoticeOpen = false;
        try
        {
            string path;
            if (view.IsDownloaded && view.FullPath is not null)
            {
                path = view.FullPath;
            }
            else if (view.Source is not null)
            {
                path = await _store.DownloadAsync(view.Source.FullUrl, view.Source.FullFileName);
                Items = Items.Select(i => ReferenceEquals(i, view)
                    ? i with { IsDownloaded = true, FullPath = path }
                    : i).ToArray();
            }
            else
            {
                return;
            }

            WallpaperService.Set(path, SelectedFit?.Fit ?? WallpaperFit.Fill);
            NoticeText = $"已设置壁纸：{view.Title}";
            NoticeOpen = true;
        }
        catch (Exception ex)
        {
            NoticeText = $"设置失败：{ex.Message}";
            NoticeOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SetLocal()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp",
            Title = "选择壁纸图片",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            WallpaperService.Set(dialog.FileName, SelectedFit?.Fit ?? WallpaperFit.Fill);
            NoticeText = $"已设置壁纸：{Path.GetFileName(dialog.FileName)}";
            NoticeOpen = true;
        }
        catch (Exception ex)
        {
            NoticeText = $"设置失败：{ex.Message}";
            NoticeOpen = true;
        }
    }

    private static string FormatDate(string endDate)
        => DateTime.TryParseExact(endDate, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d)
            ? $"{d.Month}月{d.Day}日 {d:ddd}"
            : endDate;
}
