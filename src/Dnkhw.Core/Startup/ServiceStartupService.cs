using System.Management;

namespace Dnkhw.Core.Startup;

public sealed record ServiceStartupInfo(
    string Name,
    string DisplayName,
    string StateText,
    string PathText,
    string Account);

/// <summary>
/// 自启动系统服务枚举（只读）：WMI Win32_Service StartMode=Auto。
/// 服务的启停影响系统稳定性，本工具只展示，不提供开关。
/// </summary>
public static class ServiceStartupService
{
    public static IReadOnlyList<ServiceStartupInfo> GetAutoStartServices()
    {
        var results = new List<ServiceStartupInfo>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DisplayName, PathName, State, StartName FROM Win32_Service WHERE StartMode = 'Auto'");
        using var items = searcher.Get();

        foreach (ManagementObject service in items)
        {
            try
            {
                results.Add(new ServiceStartupInfo(
                    Name: (string)service["Name"],
                    DisplayName: (string)(service["DisplayName"] ?? string.Empty),
                    StateText: StateText((string)(service["State"] ?? string.Empty)),
                    PathText: (string)(service["PathName"] ?? string.Empty),
                    Account: (string)(service["StartName"] ?? string.Empty)));
            }
            finally
            {
                service.Dispose();
            }
        }

        return results
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static string StateText(string rawState) => rawState.Trim() switch
    {
        "Running" => "运行中",
        "Stopped" => "已停止",
        "Start Pending" => "启动中",
        "Stop Pending" => "停止中",
        "Paused" => "已暂停",
        _ => rawState,
    };
}
