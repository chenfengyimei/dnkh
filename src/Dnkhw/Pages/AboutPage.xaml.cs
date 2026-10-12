using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Dnkhw.Core;
using Dnkhw.Core.Update;
using Dnkhw.Services;

namespace Dnkhw.Pages;

public partial class AboutPage : Page
{
    private const string BilibiliProfile = "https://space.bilibili.com/508302628";

    private readonly UpdateChecker _updateChecker = new();
    private bool _updateFound;

    public AboutPage()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "版本 0.1.0" : $"版本 {version.Major}.{version.Minor}.{version.Build}";
    }

    private void OnOpenSiteClick(object sender, RoutedEventArgs e)
    {
        OpenExternalLink(ProductLinks.OfficialSite);
    }

    private void OnOpenSiteInAppClick(object sender, RoutedEventArgs e)
    {
        AppNavigation.Go<WebsitePage>();
    }

    private void OnOpenBilibiliClick(object sender, RoutedEventArgs e)
    {
        OpenExternalLink(BilibiliProfile);
    }

    /// <summary>
    /// 手动检查更新：发现新版本后按钮变为「前往官网下载」，再点一次即打开官网下载页。
    /// </summary>
    private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_updateFound)
        {
            OpenExternalLink(ProductLinks.DownloadPage);
            return;
        }

        if (!UpdateRing.Visibility.Equals(Visibility.Collapsed))
        {
            return; // 正在检查中
        }

        CheckUpdateButton.IsEnabled = false;
        UpdateRing.Visibility = Visibility.Visible;
        UpdateStatusText.Visibility = Visibility.Collapsed;
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var current = version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
            var result = await _updateChecker.CheckAsync(current);

            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                    _updateFound = true;
                    UpdateStatusText.Text = $"发现新版本 v{result.Info!.Version}{(string.IsNullOrEmpty(result.Info.Notes) ? "" : $"：{result.Info.Notes}")}";
                    CheckUpdateButton.Content = "前往官网下载  ↗";
                    break;
                case UpdateCheckStatus.FetchFailed:
                    UpdateStatusText.Text = $"检查失败：{result.Error}";
                    break;
                default:
                    UpdateStatusText.Text = $"当前已是最新版本（v{current}）";
                    break;
            }

            UpdateStatusText.Visibility = Visibility.Visible;
        }
        finally
        {
            UpdateRing.Visibility = Visibility.Collapsed;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private static void OpenExternalLink(string url)
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
