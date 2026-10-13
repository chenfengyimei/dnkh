using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Dnkhw.Services;
using Wpf.Ui.Appearance;

namespace Dnkhw.Pages;

/// <summary>
/// 电脑开荒网：WebView2 内嵌官网 app.cyhaoka.vip，无需浏览器即可访问；
/// 站外链接与弹窗交给系统默认浏览器打开，网页数据目录固定在 %LOCALAPPDATA%\dnkhw。
/// </summary>
public partial class WebsitePage : Page
{
    private const string RuntimeDownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    /// <summary>内嵌站点：只放行该域名与子域，其余一律交系统浏览器。</summary>
    private static string SiteUrl => Dnkhw.Core.ProductLinks.OfficialSite;

    /// <summary>从官网地址派生的裸域名（去掉 www. 前缀），供内嵌放行判断。</summary>
    private static string OwnDomain
    {
        get
        {
            var host = new Uri(SiteUrl).Host;
            return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? host["www.".Length..]
                : host;
        }
    }

    private bool _initialized;
    private bool _titleShown;

    public WebsitePage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // 页面实例被导航缓存复用，只初始化一次
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        string? runtimeVersion = null;
        try
        {
            runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception ex)
        {
            // 查询异常按运行时缺失处理，走回退卡
            LogService.Warn("WebView2 运行时版本查询失败，按缺失处理", ex);
        }

        if (string.IsNullOrEmpty(runtimeVersion))
        {
            ShowFallback();
            return;
        }

        try
        {
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dnkhw", "webview2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
            Web.DefaultBackgroundColor = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark
                ? System.Drawing.Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F)
                : System.Drawing.Color.White;
            await Web.EnsureCoreWebView2Async(environment);

            var core = Web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.NavigationCompleted += OnNavigationCompleted;
            core.HistoryChanged += OnHistoryChanged;
            core.DocumentTitleChanged += OnDocumentTitleChanged;

            Web.Visibility = Visibility.Visible;
            core.Navigate(SiteUrl);
        }
        catch (Exception ex)
        {
            LogService.Warn("WebView2 初始化失败，显示回退卡", ex);
            ShowFallback();
        }
    }

    /// <summary>非 http(s) 协议与站外域名不进内嵌视图，交给系统浏览器。</summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
        {
            e.Cancel = true;
            return;
        }

        var isHttp = uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                     || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        var isOwnSite = uri.Host.Equals(OwnDomain, StringComparison.OrdinalIgnoreCase)
                        || uri.Host.EndsWith("." + OwnDomain, StringComparison.OrdinalIgnoreCase);
        if (!isHttp || !isOwnSite)
        {
            e.Cancel = true;
            OpenExternal(e.Uri);
            return;
        }

        // 只有真正会发生的站内导航才进入加载态；被取消的导航不会有
        // NavigationCompleted，提前显示会造成转圈永远不收。
        _titleShown = false;
        LoadRing.Visibility = Visibility.Visible;
        StatusText.Text = "正在加载…";
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // target=_blank 一律不开内嵌新窗口，走系统浏览器
        e.Handled = true;
        if (!string.IsNullOrEmpty(e.Uri))
        {
            OpenExternal(e.Uri);
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        LoadRing.Visibility = Visibility.Collapsed;
        if (!e.IsSuccess)
        {
            StatusText.Text = "加载失败——请检查网络后点「刷新」重试";
            return;
        }

        if (!_titleShown)
        {
            StatusText.Text = new Uri(SiteUrl).Host;
        }
    }

    private void OnHistoryChanged(object? sender, object e)
    {
        var core = Web.CoreWebView2;
        if (core is null)
        {
            return;
        }

        BackButton.IsEnabled = core.CanGoBack;
        ForwardButton.IsEnabled = core.CanGoForward;
    }

    private void OnDocumentTitleChanged(object? sender, object e)
    {
        // 官网页外链资源较多，完整 load 事件可能滞后很久；标题一出现即视为可用，
        // 立即收起加载动画，避免页面明明已可交互却一直转圈。
        var title = Web.CoreWebView2?.DocumentTitle;
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        _titleShown = true;
        LoadRing.Visibility = Visibility.Collapsed;
        StatusText.Text = title;
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2?.CanGoBack == true)
        {
            Web.CoreWebView2.GoBack();
        }
    }

    private void OnForwardClick(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2?.CanGoForward == true)
        {
            Web.CoreWebView2.GoForward();
        }
    }

    private void OnHomeClick(object sender, RoutedEventArgs e)
    {
        Web.CoreWebView2?.Navigate(SiteUrl);
    }

    private void OnReloadClick(object sender, RoutedEventArgs e)
    {
        Web.CoreWebView2?.Reload();
    }

    private void OnOpenExternalClick(object sender, RoutedEventArgs e) => OpenExternal(SiteUrl);

    private void OnGetRuntimeClick(object sender, RoutedEventArgs e) => OpenExternal(RuntimeDownloadUrl);

    private void ShowFallback()
    {
        Web.Visibility = Visibility.Collapsed;
        LoadRing.Visibility = Visibility.Collapsed;
        StatusText.Text = "内嵌浏览组件不可用";
        FallbackCard.Visibility = Visibility.Visible;
    }

    private static void OpenExternal(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // 打不开时静默，不弹窗打断使用
            LogService.Warn($"外链打开失败：{url}", ex);
        }
    }
}
