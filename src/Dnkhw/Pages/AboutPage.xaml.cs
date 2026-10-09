using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Dnkhw.Pages;

public partial class AboutPage : Page
{
    private const string OfficialSite = "https://www.cyhaoka.vip";

    public AboutPage()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "版本 0.1.0" : $"版本 {version.Major}.{version.Minor}.{version.Build}";
    }

    private void OnOpenSiteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(OfficialSite) { UseShellExecute = true });
        }
        catch
        {
            // 打不开时静默，不弹窗打断使用
        }
    }
}
