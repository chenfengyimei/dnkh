using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Dnkhw.Core.Downloads;

public sealed record BrowserDownloadInfo(
    string Name,
    bool IsInstalled,
    bool UsesSystemDefault,
    string? CustomDir,
    string SettingsUrl,
    string? ExecutablePath);

public sealed partial class BrowserDownloadDetector
{
    public IReadOnlyList<BrowserDownloadInfo> Detect()
    {
        return
        [
            DetectChromium("Chrome", Path.Combine(LocalAppData, @"Google\Chrome\User Data\Default\Preferences"), "chrome://settings/downloads", @"App Paths\chrome.exe"),
            DetectChromium("Edge", Path.Combine(LocalAppData, @"Microsoft\Edge\User Data\Default\Preferences"), "edge://settings/downloads", @"App Paths\msedge.exe"),
            DetectFirefox(),
        ];
    }

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private BrowserDownloadInfo DetectChromium(string name, string preferencesPath, string settingsUrl, string appPathsKey)
    {
        if (!File.Exists(preferencesPath))
        {
            return new BrowserDownloadInfo(name, IsInstalled: false, UsesSystemDefault: true, null, settingsUrl, null);
        }

        string? customDir = null;
        try
        {
            customDir = ChromiumPrefs.ParseDefaultDirectory(File.ReadAllText(preferencesPath));
        }
        catch
        {
            // 解析失败按跟随系统处理
        }

        return new BrowserDownloadInfo(
            name,
            IsInstalled: true,
            UsesSystemDefault: customDir is null,
            customDir,
            settingsUrl,
            ResolveExecutable(appPathsKey));
    }

    private BrowserDownloadInfo DetectFirefox()
    {
        var profilesRoot = Path.Combine(AppData, @"Mozilla\Firefox\Profiles");
        if (!Directory.Exists(profilesRoot))
        {
            return new BrowserDownloadInfo("Firefox", IsInstalled: false, UsesSystemDefault: true, null, "about:preferences#downloads", ResolveExecutable(@"App Paths\firefox.exe"));
        }

        string? customDir = null;
        try
        {
            // 取最近修改的 default-release 配置目录
            var prefsFile = Directory.EnumerateFiles(profilesRoot, "prefs.js", SearchOption.AllDirectories)
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .FirstOrDefault(f => f.Contains("default-release", StringComparison.OrdinalIgnoreCase));
            if (prefsFile is not null)
            {
                customDir = FirefoxPrefs.ParseDownloadDir(File.ReadAllText(prefsFile));
            }
        }
        catch
        {
            // 解析失败按跟随系统处理
        }

        return new BrowserDownloadInfo(
            "Firefox",
            IsInstalled: true,
            UsesSystemDefault: customDir is null,
            customDir,
            "about:preferences#downloads",
            ResolveExecutable(@"App Paths\firefox.exe"));
    }

    private static string? ResolveExecutable(string appPathsKey)
    {
        try
        {
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                using var key = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\{appPathsKey}");
                if (key?.GetValue(string.Empty) is string exe && File.Exists(exe))
                {
                    return exe;
                }
            }
        }
        catch
        {
            // 忽略注册表读取失败
        }

        return null;
    }
}

/// <summary>Chromium 系（Chrome/Edge）Preferences JSON 解析。</summary>
public static class ChromiumPrefs
{
    public static string? ParseDefaultDirectory(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("download", out var download)
            && download.TryGetProperty("default_directory", out var dir))
        {
            return dir.GetString() is { Length: > 0 } s ? s : null;
        }

        return null;
    }
}

/// <summary>Firefox prefs.js 解析。</summary>
public static partial class FirefoxPrefs
{
    [GeneratedRegex("""user_pref\("browser\.download\.dir",\s*"(?<dir>(?:[^"\\]|\\.)*)"\)""")]
    private static partial Regex DownloadDirRegex();

    [GeneratedRegex("""user_pref\("browser\.download\.folderList",\s*(?<list>\d+)\)""")]
    private static partial Regex FolderListRegex();

    /// <summary>
    /// 解析下载目录。folderList=2 表示自定义目录；1 表示跟随系统「下载」文件夹。
    /// </summary>
    public static string? ParseDownloadDir(string prefsContent)
    {
        var folderList = 1;
        var listMatch = FolderListRegex().Match(prefsContent);
        if (listMatch.Success)
        {
            folderList = int.Parse(listMatch.Groups["list"].Value);
        }

        if (folderList != 2)
        {
            return null;
        }

        var match = DownloadDirRegex().Match(prefsContent);
        if (!match.Success)
        {
            return null;
        }

        return Unescape(match.Groups["dir"].Value);
    }

    private static string Unescape(string value)
        => value.Replace("\\\"", "\"").Replace("\\\\", "\\");
}
