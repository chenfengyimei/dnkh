using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dnkhw.Core.AppCatalog;
using Dnkhw.Core.DesktopTweaks;
using Dnkhw.Core.Wallpaper;
using Dnkhw.Services;
using Microsoft.Win32;
namespace Dnkhw.ViewModels;

public sealed record FitOption(string Label, WallpaperFit Fit);

/// <summary>Bing 壁纸市场（不同地区的每日壁纸不同）。</summary>
public sealed record MarketOption(string Label, string Code);

public sealed record WallpaperView(
    string Title,
    string DateText,
    string? ThumbPath,
    string? FullPath,
    bool IsDownloaded,
    bool IsLocal,
    BingWallpaper? Source);

/// <summary>远端壁纸拉取结果：网络失败与「正常但无更多图片」必须区分，避免错误地关闭“加载更多”。</summary>
public sealed record WallpaperFetchResult(IReadOnlyList<BingWallpaper> Items, string? Error = null);

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

    public IReadOnlyList<MarketOption> MarketOptions { get; } =
    [
        new MarketOption("中国", "zh-CN"),
        new MarketOption("国际 (en-US)", "en-US"),
        new MarketOption("日本", "ja-JP"),
        new MarketOption("英国", "en-GB"),
        new MarketOption("德国", "de-DE"),
    ];

    /// <summary>Bing 接口已加载到的页偏移（0=今天起，每页 8 天）。</summary>
    private int _pageOffset;

    [ObservableProperty]
    private FitOption? _selectedFit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListButtonLabel))]
    private bool _isBusy;

    [ObservableProperty]
    private IReadOnlyList<WallpaperView> _items = [];

    /// <summary>壁纸列表是否已加载过（按需加载：首次点「加载壁纸列表」后才显示网格）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListButtonLabel))]
    private bool _hasLoaded;

    /// <summary>选择二加载按钮文字：未加载「加载壁纸列表」/ 加载中「正在加载…」/ 已加载「刷新列表」。</summary>
    public string ListButtonLabel => IsBusy ? "正在加载…" : HasLoaded ? "刷新列表" : "加载壁纸列表";

    [ObservableProperty]
    private MarketOption? _selectedMarket;

    /// <summary>是否还能往前翻页（Bing 最多回看约 16 天，两页）。</summary>
    [ObservableProperty]
    private bool _canLoadMore;

    partial void OnSelectedMarketChanged(MarketOption? value)
    {
        // 切换市场后重拉第一页（构造期首次赋值不触发）
        if (HasLoaded && !IsBusy)
        {
            SafeFireAndForget.Run("切换壁纸市场", () => LoadAsync());
        }
    }

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
        _selectedMarket = MarketOptions[0];
        LoadTweakStates();
    }

    /// <summary>读取桌面美化的当前系统状态（图标隐藏 / 任务栏自动隐藏 / TranslucentTB、Steam 安装情况）。</summary>
    private void LoadTweakStates()
    {
        try
        {
            DesktopIconsHidden = DesktopTweaksService.GetDesktopIconsHidden();
        }
        catch (Exception ex)
        {
            // 读取失败保持默认 false
            LogService.Warn("读取桌面图标隐藏状态失败", ex);
        }

        try
        {
            TaskbarAutoHide = DesktopTweaksService.GetTaskbarAutoHide();
        }
        catch (Exception ex)
        {
            // 读取失败保持默认 false
            LogService.Warn("读取任务栏自动隐藏状态失败", ex);
        }

        try
        {
            TranslucentInstalled = InstallDetector.IsInstalled(
                "TranslucentTB",
                InstallDetector.GetInstalledDisplayNames());
        }
        catch (Exception ex)
        {
            TranslucentInstalled = false;
            LogService.Warn("检测 TranslucentTB 安装状态失败", ex);
        }

        try
        {
            SteamInstalled = InstallDetector.IsInstalled(
                "Steam",
                InstallDetector.GetInstalledDisplayNames());
        }
        catch (Exception ex)
        {
            SteamInstalled = false;
            LogService.Warn("检测 Steam 安装状态失败", ex);
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
            LogService.Info("Steam 协议未注册，壁纸引擎商店页回退浏览器打开");
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
            _pageOffset = 0;
            var fetch = await FetchRemoteAsync(0);
            var remoteViews = await BuildViewsAsync(fetch.Items);
            var recentNames = remoteViews
                .Select(v => v.FullPath is null ? string.Empty : Path.GetFileName(v.FullPath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Items = [.. remoteViews, .. LocalCacheExtras(recentNames)];
            HasLoaded = true;
            CanLoadMore = remoteViews.Count > 0;
            if (fetch.Error is not null)
            {
                NoticeText = $"Bing 接口不可用（{fetch.Error}），仅显示本地缓存";
                NoticeOpen = true;
            }
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

    /// <summary>往前再翻一页（每页 8 天，Bing 最多回看约 16 天）。</summary>
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsBusy || !CanLoadMore)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var nextOffset = _pageOffset + 8;
            var fetch = await FetchRemoteAsync(nextOffset);
            if (fetch.Error is not null)
            {
                // 网络暂时失败不等于 Bing 历史壁纸已经耗尽，保留重试入口。
                NoticeText = $"加载更多失败：{fetch.Error}（可稍后重试）";
                NoticeOpen = true;
                return;
            }

            _pageOffset = nextOffset;

            var newViews = await BuildViewsAsync(fetch.Items);
            var existingNames = Items
                .Where(i => !i.IsLocal && i.FullPath is not null)
                .Select(i => Path.GetFileName(i.FullPath!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var fresh = newViews
                .Where(v => v.FullPath is not null && !existingNames.Contains(Path.GetFileName(v.FullPath)))
                .ToList();

            if (fresh.Count == 0)
            {
                CanLoadMore = false;
                NoticeText = "Bing 最多保留约 16 天壁纸，已全部加载";
                NoticeOpen = true;
                return;
            }

            var freshPaths = fresh.Select(v => v.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var remoteItems = Items.Where(i => !i.IsLocal).ToList();
            var localItems = Items
                .Where(i => i.IsLocal && (i.FullPath is null || !freshPaths.Contains(i.FullPath)))
                .ToList();

            // 新页插在远程壁纸之后、本地缓存补充之前
            Items = [.. remoteItems, .. fresh, .. localItems];

            // idx 上限 15：偏移 8 后再翻需要 16，已不可整页回看
            CanLoadMore = _pageOffset + 8 <= 15;
            if (!CanLoadMore)
            {
                NoticeText = "已加载 Bing 可回看的全部壁纸（约 16 天）";
                NoticeOpen = true;
            }
        }
        catch (Exception ex)
        {
            CanLoadMore = false;
            NoticeText = $"加载更多失败：{ex.Message}";
            NoticeOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<WallpaperFetchResult> FetchRemoteAsync(int offset)
    {
        try
        {
            return new WallpaperFetchResult(
                await _bing.GetRecentAsync(8, offset, SelectedMarket?.Code ?? "zh-CN"));
        }
        catch (Exception ex)
        {
            return new WallpaperFetchResult([], ex.Message);
        }
    }

    private async Task<List<WallpaperView>> BuildViewsAsync(IReadOnlyList<BingWallpaper> list)
    {
        var items = new List<WallpaperView>();
        foreach (var w in list)
        {
            string? thumbPath = null;
            try
            {
                thumbPath = await _store.DownloadThumbAsync(w);
            }
            catch (Exception ex)
            {
                // 缩略图下载失败仅影响预览
                LogService.Warn($"下载壁纸缩略图失败：{w.Title}", ex);
            }

            var fullPath = _store.GetFullPath(w.FullFileName);
            items.Add(new WallpaperView(
                w.Title,
                FormatDate(w.EndDate),
                thumbPath,
                fullPath,
                File.Exists(fullPath),
                IsLocal: false,
                Source: w));
        }

        return items;
    }

    /// <summary>本地缓存中不在远程列表内的文件（离线可用补充）。</summary>
    private List<WallpaperView> LocalCacheExtras(HashSet<string> recentNames)
    {
        var extras = new List<WallpaperView>();
        foreach (var file in _store.GetCachedFiles())
        {
            if (!recentNames.Contains(Path.GetFileName(file)))
            {
                extras.Add(new WallpaperView(
                    Path.GetFileNameWithoutExtension(file),
                    string.Empty,
                    file,
                    file,
                    IsDownloaded: true,
                    IsLocal: true,
                    Source: null));
            }
        }

        return extras;
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
