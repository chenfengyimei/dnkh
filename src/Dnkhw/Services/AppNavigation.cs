using Dnkhw.Pages;
using Wpf.Ui.Controls;

namespace Dnkhw.Services;

/// <summary>
/// 应用内导航助手：供任意页面发起页面跳转。由 MainWindow 在加载时初始化。
/// </summary>
public static class AppNavigation
{
    private static NavigationView? _navigationView;

    /// <summary>页面类型 → 持久化 key（与 --page 参数同一套名字）。</summary>
    internal static readonly IReadOnlyDictionary<Type, string> PageKeys = new Dictionary<Type, string>
    {
        [typeof(CheckPage)] = "check",
        [typeof(WallpaperPage)] = "wallpaper",
        [typeof(DefaultPathsPage)] = "downloads",
        [typeof(AppsPage)] = "apps",
        [typeof(StartupPage)] = "startup",
        [typeof(OptimizePage)] = "optimize",
        [typeof(DiskPage)] = "disk",
        [typeof(WebsitePage)] = "website",
        [typeof(AboutPage)] = "about",
        [typeof(SettingsPage)] = "settings",
    };

    internal static void Initialize(NavigationView navigationView) => _navigationView = navigationView;

    public static void Go(Type pageType) => _navigationView?.Navigate(pageType);

    public static void Go<TPage>() where TPage : class => Go(typeof(TPage));

    /// <summary>导航成功后记住页面 key，供下次启动直达（含侧边栏点击）。</summary>
    internal static void RememberPage(Type pageType)
    {
        if (!PageKeys.TryGetValue(pageType, out var key))
        {
            return;
        }

        var settings = AppSettingsService.Current;
        if (!settings.RememberLastPage || settings.LastPageKey == key)
        {
            return;
        }

        settings.LastPageKey = key;
        try
        {
            AppSettingsService.Save();
        }
        catch (Exception ex)
        {
            LogService.Warn("记住上次页面失败", ex);
        }
    }
}
