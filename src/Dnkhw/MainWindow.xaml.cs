using System.Security.Principal;
using System.Windows;
using Dnkhw.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Dnkhw;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this);
        UpdateAdminBadge();
    }

    private void RootNavigation_Loaded(object sender, RoutedEventArgs e)
    {
        AppNavigation.Initialize(RootNavigation);
        RootNavigation.SetPageProviderService(new PageProviderService());
        RootNavigation.Navigate(ResolveInitialPage());
    }

    /// <summary>
    /// 解析 --page 启动参数（check/wallpaper/downloads/apps/startup/optimize/disk/website/about），
    /// 用于采集与调试直跳指定页面；未指定时进入开荒检查。
    /// </summary>
    private static Type ResolveInitialPage()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--page");
        if (index < 0 || index + 1 >= args.Length)
        {
            return typeof(Pages.CheckPage);
        }

        return args[index + 1].ToLowerInvariant() switch
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
            _ => typeof(Pages.CheckPage),
        };
    }

    private void UpdateAdminBadge()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        bool isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        AdminBadge.Content = isAdmin
            ? "管理员权限"
            : "普通权限（部分功能需提权）";
    }
}
