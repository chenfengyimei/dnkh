using System.IO;

namespace Dnkhw.Services;

/// <summary>
/// 首次运行标记：%APPDATA%\dnkhw\first-run.flag。
/// 存在即跳过启动动画；调试可用 --splash 参数强制播放。
/// </summary>
public static class FirstRunService
{
    private static string FlagPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "dnkhw",
        "first-run.flag");

    public static bool IsFirstRun() => !File.Exists(FlagPath);

    public static void MarkDone()
    {
        var directory = Path.GetDirectoryName(FlagPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(FlagPath, DateTime.UtcNow.ToString("O"));
    }
}
