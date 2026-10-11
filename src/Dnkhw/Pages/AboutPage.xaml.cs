using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Dnkhw.Services;

namespace Dnkhw.Pages;

public partial class AboutPage : Page
{
    private const string OfficialSite = "https://www.cyhaoka.vip";
    private const string BilibiliProfile = "https://space.bilibili.com/508302628";

    public AboutPage()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "版本 0.1.0" : $"版本 {version.Major}.{version.Minor}.{version.Build}";
    }

    private void OnOpenSiteClick(object sender, RoutedEventArgs e)
    {
        OpenExternalLink(OfficialSite);
    }

    private void OnOpenSiteInAppClick(object sender, RoutedEventArgs e)
    {
        AppNavigation.Go<WebsitePage>();
    }

    private void OnOpenBilibiliClick(object sender, RoutedEventArgs e)
    {
        OpenExternalLink(BilibiliProfile);
    }

    private static void OpenExternalLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // 打不开时静默，不弹窗打断使用
        }
    }
}
