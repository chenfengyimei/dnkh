using System.Windows;
using Dnkhw.Core.Update;
using Dnkhw.Services;

namespace Dnkhw;

public partial class App : Application
{
    /// <summary>单实例互斥体：在线更新重启前需先释放，避免新进程撞「已在运行」。</summary>
    internal static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 单实例守卫：启动项/优化/迁移等写注册表类操作不允许并发
        _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\dnkhw.single-instance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("电脑开荒已在运行中。", "电脑开荒", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        RegisterGlobalExceptionHandlers();

        // 上次在线升级的残留清理（旧 .old 与过期下载包），失败无害
        SelfUpdater.CleanupOldBinary();

        // 首次运行（或调试用 --splash 参数）播放启动动画，之后直达主窗口
        var forceSplash = e.Args.Contains("--splash", StringComparer.OrdinalIgnoreCase);
        if (forceSplash || FirstRunService.IsFirstRun())
        {
            // 动画窗关闭到主窗口显示之间存在 0 窗口瞬间，改显式关闭防止应用提前退出
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                new SplashWindow().ShowDialog();
            }
            catch (Exception ex)
            {
                // 启动动画异常不能拦截主程序
                LogService.Warn("启动动画播放失败", ex);
            }

            try
            {
                FirstRunService.MarkDone();
            }
            catch (Exception ex)
            {
                // 标记写失败时下次仍会播放，可接受
                LogService.Warn("首次运行标记写入失败", ex);
            }
        }

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        LogService.Info($"启动完成 v{typeof(App).Assembly.GetName().Version}");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogService.Info($"退出 code={e.ApplicationExitCode}");
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>UI 线程 / 后台任务 / 非 UI 线程三路兜底：任何异常先落日志再决定去留。</summary>
    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            LogService.Error("UI 线程未处理异常", e.Exception);
            e.Handled = AskContinueAfterError(e.Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogService.Error("后台任务未观察异常", e.Exception);
            e.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            // 终态异常：进程即将退出，至少把现场写进日志
            LogService.Error("非 UI 线程未处理异常（进程可能退出）", e.ExceptionObject as Exception);
        };
    }

    /// <summary>复制详情到剪贴板并询问用户是否继续运行；返回 true 表示继续。</summary>
    private bool AskContinueAfterError(Exception ex)
    {
        try
        {
            Clipboard.SetText($"""
                电脑开荒未处理异常
                {ex.GetType().Name}: {ex.Message}
                {ex.StackTrace}
                """);
        }
        catch
        {
            // 剪贴板可能被占用，复制失败不影响弹窗
        }

        var result = MessageBox.Show(
            MainWindow,
            $"遇到未处理的错误：{ex.Message}\n\n错误详情已复制到剪贴板，完整记录见日志文件夹。\n是否继续运行？（选择「否」将退出程序）",
            "电脑开荒",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }
}
