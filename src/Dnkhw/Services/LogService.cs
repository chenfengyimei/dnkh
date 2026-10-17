using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace Dnkhw.Services;

/// <summary>
/// 单文件诊断日志：当前日志固定为 %APPDATA%\dnkhw\logs\dnkhw.log，用户可直接发送。
/// 达到 8 MB 时仅保留一份 dnkhw.previous.log，避免长期运行无限占用空间。
/// 日志系统自身的任何失败均静默忽略，绝不能反过来拖垮主程序。
/// </summary>
public static class LogService
{
    private const long MaxLogBytes = 8L * 1024 * 1024;
    private const int LegacyRetainDays = 14;
    private static readonly object WriteLock = new();
    private static readonly string SessionId = Guid.NewGuid().ToString("N")[..8];
    private static bool _initialized;
    private static bool _sessionStarted;

    public static string LogsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "dnkhw",
        "logs");

    /// <summary>始终指向本次运行正在写入、可直接发给开发者的诊断日志。</summary>
    public static string CurrentLogPath => Path.Combine(LogsDirectory, "dnkhw.log");

    public static void StartSession()
    {
        try
        {
            lock (WriteLock)
            {
                EnsureInitialized();
                WriteSessionHeaderIfNeeded();
            }
        }
        catch
        {
            // 日志初始化失败不影响主程序启动。
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    /// <summary>直接打开当前诊断日志；若文件关联不可用则在资源管理器中选中它。</summary>
    public static void OpenCurrentLog()
    {
        StartSession();
        Info("用户从设置页打开诊断日志");

        try
        {
            Process.Start(new ProcessStartInfo(CurrentLogPath) { UseShellExecute = true });
        }
        catch
        {
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add($"/select,{CurrentLogPath}");
            Process.Start(startInfo);
        }
    }

    /// <summary>保留旧调用入口，但设置页应优先直接打开当前日志文件。</summary>
    public static void OpenInExplorer()
    {
        StartSession();
        Process.Start(new ProcessStartInfo
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
                EnsureInitialized();
                WriteSessionHeaderIfNeeded();

                var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
                var prefix = $"{timestamp} [{level}] [S:{SessionId}] [T:{Environment.CurrentManagedThreadId}] ";
                var entry = prefix + NormalizeMultiline(message, prefix.Length);
                if (ex is not null)
                {
                    entry += Environment.NewLine
                        + $"{timestamp} [{level}] [S:{SessionId}]   Exception={ex.GetType().FullName}; "
                        + $"HResult=0x{ex.HResult:X8}; Message={ex.Message}"
                        + Environment.NewLine
                        + Indent(ex.ToString(), "    ");
                }

                File.AppendAllText(CurrentLogPath, entry + Environment.NewLine);
            }
        }
        catch
        {
            // 日志写失败时无处可去，只能放弃。
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        Directory.CreateDirectory(LogsDirectory);
        RotateOversizedLog();
        CleanupLegacyDailyLogs();
        _initialized = true;
    }

    private static void WriteSessionHeaderIfNeeded()
    {
        if (_sessionStarted)
        {
            return;
        }

        _sessionStarted = true;
        var entryAssembly = Assembly.GetEntryAssembly();
        var version = entryAssembly?.GetName().Version?.ToString() ?? "unknown";
        var informationalVersion = entryAssembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? version;
        var elevated = IsElevated();
        var executable = RedactUserProfile(Environment.ProcessPath ?? "unknown");
        var commandLine = string.Join(' ', Environment.GetCommandLineArgs().Skip(1).Select(RedactUserProfile));

        var separator = new string('=', 96);
        var header = $"""
            {separator}
            {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [SESSION] Start; Id={SessionId}; PID={Environment.ProcessId}
              App=电脑开荒; Version={version}; InformationalVersion={informationalVersion}
              OS={RuntimeInformation.OSDescription}; Windows={GetWindowsRelease()}; 64BitOS={Environment.Is64BitOperatingSystem}
              Runtime={RuntimeInformation.FrameworkDescription}; ProcessArch={RuntimeInformation.ProcessArchitecture}; OSArch={RuntimeInformation.OSArchitecture}
              Elevated={elevated}; Culture={CultureInfo.CurrentCulture.Name}; UICulture={CultureInfo.CurrentUICulture.Name}
              Executable={executable}
              Arguments={(commandLine.Length == 0 ? "(none)" : commandLine)}
            {separator}
            """;
        File.AppendAllText(CurrentLogPath, header + Environment.NewLine);
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string GetWindowsRelease()
    {
        var version = Environment.OSVersion.Version;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var productName = key?.GetValue("ProductName") as string;
            var displayVersion = key?.GetValue("DisplayVersion") as string;
            var build = key?.GetValue("CurrentBuildNumber") as string ?? version.Build.ToString(CultureInfo.InvariantCulture);
            var ubr = key?.GetValue("UBR") is int revision ? revision.ToString(CultureInfo.InvariantCulture) : version.Revision.ToString(CultureInfo.InvariantCulture);
            var inferredProduct = version.Build >= 22000 ? "Windows 11" : "Windows 10";
            return $"{inferredProduct} {displayVersion ?? "(版本未知)"} Build {build}.{ubr}; RegistryProduct={productName ?? "unknown"}";
        }
        catch (Exception ex)
        {
            return $"Build {version.Build}.{version.Revision}; DetailUnavailable={ex.GetType().Name}";
        }
    }

    private static string RedactUserProfile(string value)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile)
            ? value
            : value.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMultiline(string text, int prefixLength)
    {
        var continuation = new string(' ', prefixLength);
        return string.Join(
            Environment.NewLine + continuation,
            (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));
    }

    private static string Indent(string text, string indentation) => string.Join(
        Environment.NewLine,
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => indentation + line));

    private static void RotateOversizedLog()
    {
        if (!File.Exists(CurrentLogPath) || new FileInfo(CurrentLogPath).Length < MaxLogBytes)
        {
            return;
        }

        var previousPath = Path.Combine(LogsDirectory, "dnkhw.previous.log");
        try
        {
            File.Delete(previousPath);
            File.Move(CurrentLogPath, previousPath);
        }
        catch
        {
            // 轮转失败时继续追加当前文件，诊断信息优先于空间上限。
        }
    }

    private static void CleanupLegacyDailyLogs()
    {
        var cutoff = DateTime.Today.AddDays(-LegacyRetainDays);
        foreach (var file in Directory.EnumerateFiles(LogsDirectory, "dnkhw-*.log"))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (stem.Length != "dnkhw-xxxxxxxx".Length
                || !DateTime.TryParseExact(
                    stem["dnkhw-".Length..],
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date)
                || date >= cutoff)
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch
            {
                // 旧日志清理失败不影响运行。
            }
        }
    }
}
