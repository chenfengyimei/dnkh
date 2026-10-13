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
    private readonly UpdateDownloader _updateDownloader = new();
    private UpdateInfo? _availableUpdate;
    private CancellationTokenSource? _updateCts;

    public AboutPage()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "版本未知" : $"版本 {version.Major}.{version.Minor}.{version.Build}";
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
    /// 手动检查更新：按 GitHub → Gitee → 官网顺序；
    /// 有新版显示「在线更新」与「前往官网下载」双通道。
    /// </summary>
    private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        if (!UpdateRing.Visibility.Equals(Visibility.Collapsed))
        {
            return; // 正在检查中
        }

        SetCheckBusy(true);
        UpdateNowButton.Visibility = Visibility.Collapsed;
        UpdateStatusText.Visibility = Visibility.Collapsed;
        DownloadProgressPanel.Visibility = Visibility.Collapsed;
        try
        {
            var current = CurrentVersionText();
            var result = await _updateChecker.CheckAsync(current);

            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                    _availableUpdate = result.Info;
                    UpdateStatusText.Text =
                        $"发现新版本 v{result.Info!.Version}{(string.IsNullOrEmpty(result.Info.Notes) ? "" : $"：{FirstLine(result.Info.Notes)}")}"
                        + $"（来源：{UpdateChecker.SourceName(result.Info.Source)}）";
                    UpdateNowButton.Visibility = string.IsNullOrWhiteSpace(result.Info.DirectUrl)
                        ? Visibility.Collapsed
                        : Visibility.Visible;
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
            SetCheckBusy(false);
        }
    }

    /// <summary>在线更新：下载 → SHA256 校验 → 确认后替换并（可选）立即重启。</summary>
    private async void OnUpdateNowClick(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is not { } info)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(info.DirectUrl) || string.IsNullOrWhiteSpace(info.Sha256))
        {
            // 无哈希源（如 Gitee）：不自动替换，引导官网
            UpdateStatusText.Text = "该更新源未提供校验哈希，已为你打开官网下载页";
            UpdateStatusText.Visibility = Visibility.Visible;
            OpenExternalLink(ProductLinks.DownloadPage);
            return;
        }

        if (_updateCts is not null)
        {
            return; // 已在下载
        }

        _updateCts = new CancellationTokenSource();
        UpdateNowButton.IsEnabled = false;
        CheckUpdateButton.IsEnabled = false;
        DownloadProgressBar.Value = 0;
        DownloadProgressPanel.Visibility = Visibility.Visible;
        UpdateStatusText.Text = "正在下载更新…";
        UpdateStatusText.Visibility = Visibility.Visible;

        var progress = new Progress<UpdateDownloadProgress>(p =>
        {
            DownloadProgressBar.Value = p.Percent;
            UpdateStatusText.Text = p.TotalBytes > 0
                ? $"正在下载 v{info.Version}：{p.Percent:F0}%（{p.SizeText}）"
                : $"正在下载 v{info.Version}：{p.SizeText}";
        });

        try
        {
            var readyPath = await _updateDownloader.DownloadAsync(info, progress, _updateCts.Token);

            var answer = MessageBox.Show(
                $"新版本 v{info.Version} 已下载并校验通过。\n是否立即重启电脑开荒以完成更新？",
                "电脑开荒",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer == MessageBoxResult.Yes)
            {
                RestartIntoUpdate(readyPath);
            }
            else
            {
                // 拒绝立即重启：先落位新文件，下次启动自动生效
                SelfUpdater.Apply(readyPath);
                UpdateStatusText.Text = $"v{info.Version} 已就绪，重启电脑开荒后生效";
            }
        }
        catch (OperationCanceledException)
        {
            UpdateStatusText.Text = "已取消下载，可稍后再点「在线更新」";
        }
        catch (Exception ex)
        {
            LogService.Error("在线更新失败", ex);
            UpdateStatusText.Text = $"在线更新失败：{ex.Message}（可前往官网手动下载）";
        }
        finally
        {
            _updateCts.Dispose();
            _updateCts = null;
            DownloadProgressPanel.Visibility = Visibility.Collapsed;
            UpdateNowButton.IsEnabled = true;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OnCancelUpdateClick(object sender, RoutedEventArgs e) => _updateCts?.Cancel();

    /// <summary>替换文件并立即启动新版本（旧窗口随进程退出关闭）。</summary>
    private void RestartIntoUpdate(string readyPath)
    {
        try
        {
            SelfUpdater.Apply(readyPath);
            if (Environment.ProcessPath is { } exePath)
            {
                // 先启动新版本再退出：单实例 Mutex 需先释放，否则新进程会撞上「已在运行」
                App._singleInstanceMutex?.ReleaseMutex();
                App._singleInstanceMutex?.Dispose();
                App._singleInstanceMutex = null;
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            }

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            LogService.Error("应用更新重启失败", ex);
            MessageBox.Show($"应用新版本时出错：{ex.Message}\n下载文件已保留，可重启应用后再试。",
                "电脑开荒", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetCheckBusy(bool busy)
    {
        CheckUpdateButton.IsEnabled = !busy;
        UpdateRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string CurrentVersionText()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\r', '\n').FirstOrDefault(l => l.Trim().Length > 0);
        return line?.Trim() ?? string.Empty;
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
