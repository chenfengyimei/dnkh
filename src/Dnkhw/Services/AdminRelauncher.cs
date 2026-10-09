using System.Security.Principal;

namespace Dnkhw.Services;

/// <summary>管理员检测与「以管理员身份重启」。</summary>
public static class AdminRelauncher
{
    public static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>弹出 UAC 以管理员重启本程序；用户取消 UAC 时静默失败。</summary>
    public static void Relaunch()
    {
        var exePath = Environment.ProcessPath;
        if (exePath is null)
        {
            return;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas",
            };
            if (System.Diagnostics.Process.Start(psi) is not null)
            {
                System.Windows.Application.Current.Shutdown();
            }
        }
        catch
        {
            // 用户取消 UAC 或启动失败：留在当前权限继续使用
        }
    }
}
