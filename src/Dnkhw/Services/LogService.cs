using System.IO;

namespace Dnkhw.Services;

/// <summary>
/// 轻量文件日志：%APPDATA%\dnkhw\logs\dnkhw-yyyyMMdd.log，按天滚动、保留 14 天。
/// 无第三方依赖；自身任何失败静默忽略——日志系统绝不能反过来拖垮主程序。
/// </summary>
public static class LogService
{
    private const int RetainDays = 14;
    private static readonly object WriteLock = new();
    private static bool _cleanupDone;

    public static string LogsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "dnkhw",
        "logs");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    public static void OpenInExplorer()
    {
        Directory.CreateDirectory(LogsDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = LogsDirectory,
            UseShellExecute = true,
        });
    }

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (WriteLock)
            {
                try
                {
                    Directory.CreateDirectory(LogsDirectory);
                    CleanupOldFilesOnce();
                }
                catch
                {
                    // 清理失败不影响本次写入
                }

                var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
                if (ex is not null)
                {
                    line += $"{Environment.NewLine}{DateTime.Now:HH:mm:ss.fff} [{level}]   {ex.GetType().Name}: {ex.Message}";
                    if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                    {
                        line += $"{Environment.NewLine}{Indent(ex.StackTrace)}";
                    }
                    if (ex.InnerException is not null)
                    {
                        line += $"{Environment.NewLine}  内部异常: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                    }
                }

                var path = Path.Combine(LogsDirectory, $"dnkhw-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // 日志写失败时无处可去，只能放弃
        }
    }

    private static string Indent(string text) => string.Join(
        Environment.NewLine,
        text.Split('\n').Select(static l => $"  {l.TrimEnd('\r')}"));

    private static void CleanupOldFilesOnce()
    {
        if (_cleanupDone || !Directory.Exists(LogsDirectory))
        {
            return;
        }

        _cleanupDone = true;
        var cutoff = DateTime.Today.AddDays(-RetainDays);
        foreach (var file in Directory.EnumerateFiles(LogsDirectory, "dnkhw-*.log"))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (stem.Length != "dnkhw-xxxxxxxx".Length
                || !DateTime.TryParseExact(stem["dnkhw-".Length..], "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var date))
            {
                continue;
            }

            if (date < cutoff)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // 清理失败不影响运行
                }
            }
        }
    }
}
