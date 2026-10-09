using Wpf.Ui.Controls;

namespace Dnkhw.Services;

/// <summary>
/// 应用内导航助手：供任意页面发起页面跳转。由 MainWindow 在加载时初始化。
/// </summary>
public static class AppNavigation
{
    private static NavigationView? _navigationView;

    internal static void Initialize(NavigationView navigationView) => _navigationView = navigationView;

    public static void Go(Type pageType) => _navigationView?.Navigate(pageType);

    public static void Go<TPage>() where TPage : class => Go(typeof(TPage));
}
