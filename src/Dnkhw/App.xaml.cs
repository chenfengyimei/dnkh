using System.Windows;
using Dnkhw.Services;

namespace Dnkhw;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
            catch
            {
                // 启动动画异常不能拦截主程序
            }

            try
            {
                FirstRunService.MarkDone();
            }
            catch
            {
                // 标记写失败时下次仍会播放，可接受
            }
        }

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }
}
