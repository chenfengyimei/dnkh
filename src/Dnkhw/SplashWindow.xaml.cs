using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Dnkhw.Core.AppCatalog;
using Dnkhw.Core.Wallpaper;

namespace Dnkhw;

/// <summary>
/// 首次运行启动动画：分阶段初始化（真实任务驱动进度条）→
/// 「电脑开荒」四字依次弹性弹出 → 淡出进入主窗口。
/// </summary>
public partial class SplashWindow : Window
{
    private readonly IReadOnlyList<(string Status, Func<Task> Work)> _stages;

    public SplashWindow()
    {
        InitializeComponent();

        var wingetPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");

        _stages =
        [
            ("加载精选软件清单…", () =>
            {
                _ = AppCatalogLoader.LoadDefault();
                return Task.CompletedTask;
            }),
            ("准备壁纸目录…", () =>
            {
                _ = new WallpaperStore();
                return Task.CompletedTask;
            }),
            ("预热系统体检…", () => Task.Run(() =>
            {
                // 轻量 WMI 预热，让首个开荒检查更快出结果
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                _ = searcher.Get().Count;
            })),
            ("检查安装组件…", () =>
            {
                _ = File.Exists(wingetPath);
                return Task.CompletedTask;
            }),
        ];
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // 阶段一：分阶段初始化，进度条平滑推进
            var step = 100.0 / (_stages.Count + 1);
            for (var i = 0; i < _stages.Count; i++)
            {
                StatusText.Text = _stages[i].Status;
                await Task.Delay(260);
                await Task.WhenAll(Task.Run(_stages[i].Work), Task.Delay(180), AnimateProgressTo((i + 1) * step));
            }

            StatusText.Text = "初始化完成";
            await AnimateProgressTo(100);

            // 阶段二：四字弹出 → 副标题与装饰条
            await AnimateLogoAsync();
            await Task.Delay(1150);

            // 阶段三：淡出关闭
            await FadeAndCloseAsync();
        }
        catch (Exception ex)
        {
            // 启动动画任何异常都不能卡住首次启动
            Trace.WriteLine($"Splash error: {ex.Message}");
            Close();
        }
    }

    private Task AnimateProgressTo(double target)
    {
        var tcs = new TaskCompletionSource();
        var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        animation.Completed += (_, _) => tcs.SetResult();
        InitProgress.BeginAnimation(ProgressBar.ValueProperty, animation);
        return tcs.Task;
    }

    private async Task AnimateLogoAsync()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        LogoPanel.Visibility = Visibility.Visible;
        LogoPanel.Opacity = 1; // XAML 初始为 0，展示时恢复，子元素动画才可见

        var chars = new[] { (Char1, Char1Scale), (Char2, Char2Scale), (Char3, Char3Scale), (Char4, Char4Scale) };
        for (var i = 0; i < chars.Length; i++)
        {
            AnimateChar(chars[i].Item1, chars[i].Item2, i * 150);
        }

        // 末段动画起始于 450ms、持续 800ms，等待全部落定
        await Task.Delay(450 + 820);

        AnimateFade(SubtitleText, 400);
        AnimateFade(AccentBar, 500);
        await Task.Delay(520);
    }

    private static void AnimateChar(TextBlock text, ScaleTransform scale, int delayMs)
    {
        var opacity = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        text.BeginAnimation(OpacityProperty, opacity);

        var elastic = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 4 };
        var scaleX = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(800))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = elastic,
        };
        var scaleY = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(800))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = elastic,
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
    }

    private static void AnimateFade(UIElement element, int durationMs)
    {
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        element.BeginAnimation(OpacityProperty, animation);
    }

    private async Task FadeAndCloseAsync()
    {
        var tcs = new TaskCompletionSource();
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(480))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        fade.Completed += (_, _) =>
        {
            tcs.SetResult();
            Close();
        };
        BeginAnimation(OpacityProperty, fade);
        await tcs.Task;
    }
}
