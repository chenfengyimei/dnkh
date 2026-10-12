using System.Security.Principal;
using System.Windows;
using Dnkhw.Services;
using Wpf.Ui.Controls;

namespace Dnkhw;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        // 主题必须在窗口 Loaded 后应用：构造期 WPF-UI 字典尚未物化，
        // 过早 Apply 会被 App.xaml 的初始字典重置（实测保存的深色不生效）
        Loaded += (_, _) => ThemeCoordinator.Attach(this);
        UpdateAdminBadge();
    }

    private void RootNavigation_Loaded(object sender, RoutedEventArgs e)
    {
        AppNavigation.Initialize(RootNavigation);
        RootNavigation.SetPageProviderService(new PageProviderService());
        RootNavigation.Navigate(ResolveInitialPage());
        RootNavigation.Navigated += OnRootNavigationNavigated;
    }

    private void OnRootNavigationNavigated(NavigationView sender, NavigatedEventArgs args)
    {
        // WPF-UI 传的是页面实例（object）；程序内跳转与侧边栏点击都在此统一记忆
        var pageType = args.Page switch
        {
            Type type => type,
            System.Windows.Controls.Page page => page.GetType(),
            _ => null,
        };
        if (pageType is not null)
        {
            AppNavigation.RememberPage(pageType);
        }
    }

    /// <summary>
    /// 解析启动页面：--page 参数（采集调试）优先；其次记忆的上次页面（设置开启时）；默认开荒检查。
    /// 未知 key 一律回退开荒检查页。
    /// </summary>
    private static Type ResolveInitialPage()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--page");

        var settings = AppSettingsService.Current;
        if (index >= 0 && index + 1 < args.Length)
        {
            return ResolvePageKey(args[index + 1].ToLowerInvariant());
        }

        if (settings.RememberLastPage && !string.IsNullOrWhiteSpace(settings.LastPageKey))
        {
            return ResolvePageKey(settings.LastPageKey.ToLowerInvariant());
        }

        return typeof(Pages.CheckPage);
    }

    private static Type ResolvePageKey(string key) => key switch
    {
        "check" => typeof(Pages.CheckPage),
        "wallpaper" => typeof(Pages.WallpaperPage),
        "downloads" => typeof(Pages.DefaultPathsPage),
        "apps" => typeof(Pages.AppsPage),
        "startup" => typeof(Pages.StartupPage),
        "optimize" => typeof(Pages.OptimizePage),
        "disk" => typeof(Pages.DiskPage),
        "website" => typeof(Pages.WebsitePage),
        "about" => typeof(Pages.AboutPage),
        "settings" => typeof(Pages.SettingsPage),
        _ => typeof(Pages.CheckPage),
    };

    private void UpdateAdminBadge()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        bool isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        AdminBadge.Content = isAdmin
            ? "管理员权限"
            : "普通权限（部分功能需提权）";
    }
}
