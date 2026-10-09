using Microsoft.Win32;

namespace Dnkhw.Core.AppCatalog;

/// <summary>
/// 已安装检测：读取卸载注册表的 DisplayName 列表，与清单名称做双向包含匹配。
/// </summary>
public static class InstallDetector
{
    private static readonly string[] UninstallKeys =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    /// <summary>采集本机全部已安装程序显示名（HKLM 64/32 位 + HKCU）。</summary>
    public static IReadOnlyList<string> GetInstalledDisplayNames()
    {
        var names = new List<string>();
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var keyPath in UninstallKeys)
            {
                using var key = hive.OpenSubKey(keyPath);
                if (key is null)
                {
                    continue;
                }

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey?.GetValue("DisplayName") is string name && !string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }
            }
        }

        return names;
    }

    /// <summary>名称匹配：忽略大小写下双向包含（清单名 ⊂ 注册名 或 注册名 ⊂ 清单名）。</summary>
    public static bool IsInstalled(string appName, IEnumerable<string> installedNames)
    {
        foreach (var installed in installedNames)
        {
            if (installed.Contains(appName, StringComparison.OrdinalIgnoreCase)
                || appName.Contains(installed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
