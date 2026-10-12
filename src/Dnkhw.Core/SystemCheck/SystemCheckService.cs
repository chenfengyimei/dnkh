using System.Management;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Dnkhw.Core.Formatting;
using Microsoft.Win32;

namespace Dnkhw.Core.SystemCheck;

/// <summary>
/// 开荒检查服务：并行采集系统概览与健康项。全部为只读查询，不修改任何系统状态。
/// WMI 行在查询时即物化为字典，避免在集合释放后访问 COM 对象。
/// </summary>
public sealed class SystemCheckService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private static readonly TimeSpan DefaultWmiTimeout = TimeSpan.FromSeconds(10);

    #region 概览

    public async Task<SystemOverview> GetOverviewAsync(CancellationToken ct = default)
    {
        var cpuTask = TryWmiAsync("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
        var gpuTask = TryWmiAsync("SELECT Name, CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController");
        var osTask = TryWmiAsync("SELECT TotalVisibleMemorySize, LastBootUpTime, InstallDate FROM Win32_OperatingSystem");
        var boardTask = TryWmiAsync("SELECT Manufacturer, Product FROM Win32_BaseBoard");
        var biosTask = TryWmiAsync("SELECT Manufacturer, SMBIOSBIOSVersion FROM Win32_BIOS");
        var csTask = TryWmiAsync("SELECT Manufacturer, Model, PCSystemType FROM Win32_ComputerSystem");

        var all = await Task.WhenAll(cpuTask, gpuTask, osTask, boardTask, biosTask, csTask).ConfigureAwait(false);
        var (osName, edition, displayVersion, build) = GetOsInfo();

        var cpuRow = all[0].FirstOrDefault();
        var cpu = "未知";
        if (cpuRow is not null)
        {
            var cpuName = cpuRow.GetValueOrDefault("Name")?.ToString()?.Trim();
            cpu = string.IsNullOrWhiteSpace(cpuName) ? "未知" : $"{cpuName}（{cpuRow.GetValueOrDefault("NumberOfCores")} 核 {cpuRow.GetValueOrDefault("NumberOfLogicalProcessors")} 线程）";
        }

        var gpuNames = all[1]
            .Select(r => r.GetValueOrDefault("Name")?.ToString()?.Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct()
            .ToList();

        // 过滤远程办公 / 模拟器 / 串流等虚拟显卡，只保留物理 GPU；全部被过滤时回退原列表
        var virtualMarkers = new[] { "Virtual", "IddDriver", "Idd", "MuMu", "GameViewer", "Oray" };
        var physicalGpus = gpuNames
            .Where(n => !virtualMarkers.Any(m => n.Contains(m, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var displayGpus = physicalGpus.Count > 0 ? physicalGpus : gpuNames;
        var gpu = displayGpus.Count > 0 ? string.Join(" + ", displayGpus) : "未知";

        var resRow = all[1].FirstOrDefault(r => r.GetValueOrDefault("CurrentHorizontalResolution") is not null);
        var resolution = resRow is not null
            ? $"{resRow.GetValueOrDefault("CurrentHorizontalResolution")} × {resRow.GetValueOrDefault("CurrentVerticalResolution")}"
            : "未知";

        var osRow = all[2].FirstOrDefault();
        var ramBytes = osRow is not null && osRow.GetValueOrDefault("TotalVisibleMemorySize") is not null
            ? Convert.ToInt64(osRow.GetValueOrDefault("TotalVisibleMemorySize")) * 1024
            : 0;

        var uptime = "未知";
        try
        {
            if (osRow?.GetValueOrDefault("LastBootUpTime")?.ToString() is { Length: > 0 } boot)
            {
                uptime = FormatUptime(DateTime.Now - ManagementDateTimeConverter.ToDateTime(boot));
            }
        }
        catch
        {
            // 忽略开机时长解析失败
        }

        var installDate = "未知";
        try
        {
            if (osRow?.GetValueOrDefault("InstallDate")?.ToString() is { Length: > 0 } install)
            {
                installDate = ManagementDateTimeConverter.ToDateTime(install).ToString("yyyy-MM-dd");
            }
        }
        catch
        {
            // 忽略安装日期解析失败
        }

        // 电脑类型：Win32_ComputerSystem.PCSystemType 1=台式机 2=笔记本
        var csRow = all[5].FirstOrDefault();
        var computerType = "未知";
        if (csRow?.GetValueOrDefault("PCSystemType") is not null)
        {
            computerType = Convert.ToInt32(csRow.GetValueOrDefault("PCSystemType")) switch
            {
                2 => "笔记本",
                1 => "台式机",
                _ => "未知",
            };
        }

        var systemModel = JoinParts(csRow?.GetValueOrDefault("Manufacturer")?.ToString(), csRow?.GetValueOrDefault("Model")?.ToString());
        var baseBoardRow = all[3].FirstOrDefault();
        var baseBoard = JoinParts(baseBoardRow?.GetValueOrDefault("Manufacturer")?.ToString(), baseBoardRow?.GetValueOrDefault("Product")?.ToString());
        var biosRow = all[4].FirstOrDefault();
        var biosInfo = biosRow is null ? "未知" : JoinParts(biosRow.GetValueOrDefault("Manufacturer")?.ToString(), biosRow.GetValueOrDefault("SMBIOSBIOSVersion")?.ToString());

        var disks = new List<DiskInfo>();
        try
        {
            disks = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => new DiskInfo(
                    d.Name,
                    string.IsNullOrWhiteSpace(d.VolumeLabel) ? (d.RootDirectory.FullName.StartsWith("C", StringComparison.OrdinalIgnoreCase) ? "系统盘" : "本地磁盘") : d.VolumeLabel,
                    d.TotalSize,
                    d.AvailableFreeSpace))
                .ToList();
        }
        catch
        {
            // 磁盘枚举失败时保留空列表
        }

        return new SystemOverview(
            OsName: osName,
            Edition: edition,
            DisplayVersion: displayVersion,
            Build: build,
            ComputerType: computerType,
            SystemModel: systemModel,
            BaseBoard: baseBoard,
            BiosInfo: biosInfo,
            Cpu: cpu,
            Gpu: gpu,
            RamBytes: ramBytes,
            Resolution: resolution,
            HostName: Environment.MachineName,
            Uptime: uptime,
            InstallDate: installDate,
            Disks: disks);
    }

    private static string JoinParts(string? a, string? b)
    {
        var parts = new[] { a?.Trim(), b?.Trim() }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        return parts.Count > 0 ? string.Join(" ", parts) : "未知";
    }

    private static (string Name, string Edition, string DisplayVersion, string Build) GetOsInfo()
    {
        try
        {
            using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var productName = (string?)cv?.GetValue("ProductName") ?? "Windows";
            var displayVersion = (string?)cv?.GetValue("DisplayVersion") ?? "";
            var currentBuild = (string?)cv?.GetValue("CurrentBuild") ?? "";
            var ubr = cv?.GetValue("UBR")?.ToString();
            var edition = MapEdition((string?)cv?.GetValue("EditionID"));
            var build = string.IsNullOrEmpty(ubr) ? currentBuild : $"{currentBuild}.{ubr}";

            // Win11 部分构建的注册表 ProductName 仍写作 Windows 10，按构建号校正
            var name = int.TryParse(currentBuild, out var b) && b >= 22000 ? "Windows 11" : productName;
            return (name, edition, displayVersion, build);
        }
        catch
        {
            return ("Windows", "", "", "");
        }
    }

    private static string MapEdition(string? editionId) => editionId switch
    {
        "Core" => "家庭版",
        "CoreCountrySpecific" => "家庭中文版",
        "CoreSingleLanguage" => "家庭单语言版",
        "Professional" => "专业版",
        "ProfessionalEducation" => "专业教育版",
        "ProfessionalWorkstation" => "专业工作站版",
        "Enterprise" => "企业版",
        "Education" => "教育版",
        "IoTEnterprise" => "IoT 企业版",
        _ => editionId ?? "",
    };

    private static string FormatUptime(TimeSpan t) => t.Days > 0
        ? $"{t.Days} 天 {t.Hours} 小时"
        : t.Hours > 0 ? $"{t.Hours} 小时 {t.Minutes} 分钟" : $"{t.Minutes} 分钟";

    #endregion

    #region 检查项

    public async Task<IReadOnlyList<CheckItem>> RunChecksAsync(CancellationToken ct = default)
    {
        var items = await Task.WhenAll(
            CheckActivationAsync(),
            CheckDefenderAsync(),
            CheckFirewallAsync(),
            CheckProblemDevicesAsync(),
            CheckPendingRebootAsync(),
            CheckNetworkAsync(ct),
            CheckPowerPlanAsync(ct),
            CheckHiberfilAsync(),
            CheckSystemDiskAsync()).ConfigureAwait(false);
        return items;
    }

    private static async Task<CheckItem> CheckActivationAsync()
    {
        try
        {
            var rows = await QueryWmiAsync(
                "SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL AND Name LIKE 'Windows%'",
                timeout: TimeSpan.FromSeconds(25)).ConfigureAwait(false);

            var status = rows
                .Select(r => r.GetValueOrDefault("LicenseStatus"))
                .Where(v => v is not null)
                .Select(Convert.ToInt32)
                .DefaultIfEmpty(-1)
                .First();

            return status switch
            {
                1 => new CheckItem("activation", "系统激活", CheckStatus.Ok, "Windows 已激活"),
                0 or -1 => new CheckItem("activation", "系统激活", CheckStatus.Warn, "未检测到激活信息", "前往「设置 > 系统 > 激活」查看与处理"),
                _ => new CheckItem("activation", "系统激活", CheckStatus.Warn, $"激活状态异常（LicenseStatus={status}）", "前往「设置 > 系统 > 激活」查看"),
            };
        }
        catch (Exception ex)
        {
            return ErrorItem("activation", "系统激活", ex);
        }
    }

    private static async Task<CheckItem> CheckDefenderAsync()
    {
        try
        {
            var rows = await QueryWmiAsync(
                "SELECT AntivirusEnabled, RealTimeProtectionEnabled, AntivirusSignatureLastUpdated FROM MSFT_MpComputerStatus",
                scope: @"root\Microsoft\Windows\Defender").ConfigureAwait(false);

            var row = rows.FirstOrDefault();
            if (row is null)
            {
                return new CheckItem("defender", "安全防护", CheckStatus.Info, "未检测到 Windows Defender（可能使用第三方杀毒软件）");
            }

            var avEnabled = Convert.ToBoolean(row.GetValueOrDefault("AntivirusEnabled"));
            var rtpEnabled = Convert.ToBoolean(row.GetValueOrDefault("RealTimeProtectionEnabled"));

            DateTime? sigTime = null;
            try
            {
                if (row.GetValueOrDefault("AntivirusSignatureLastUpdated")?.ToString() is { Length: > 0 } sig)
                {
                    sigTime = ManagementDateTimeConverter.ToDateTime(sig);
                }
            }
            catch
            {
                // 病毒库时间解析失败不影响主判断
            }

            if (!avEnabled || !rtpEnabled)
            {
                return new CheckItem("defender", "安全防护", CheckStatus.Warn,
                    $"Defender 防病毒：{(avEnabled ? "开" : "关")}，实时保护：{(rtpEnabled ? "开" : "关")}",
                    "建议开启实时保护（设置 > 隐私和安全性 > Windows 安全中心）");
            }

            var sigDetail = sigTime is null ? "" : $"，病毒库更新于 {sigTime:yyyy-MM-dd HH:mm}";
            var sigStale = sigTime is not null && (DateTime.Now - sigTime.Value).TotalDays > 7;
            return sigStale
                ? new CheckItem("defender", "安全防护", CheckStatus.Warn, $"实时保护已开启{sigDetail}", "病毒库超过 7 天未更新，建议立即更新")
                : new CheckItem("defender", "安全防护", CheckStatus.Ok, $"实时保护已开启{sigDetail}");
        }
        catch
        {
            return new CheckItem("defender", "安全防护", CheckStatus.Info, "Defender 状态查询不可用（可能使用第三方杀毒软件）");
        }
    }

    private static Task<CheckItem> CheckFirewallAsync() => Task.Run(() =>
    {
        object? firewall = null;
        try
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type is null)
            {
                return new CheckItem("firewall", "防火墙", CheckStatus.Info, "无法创建防火墙 COM 组件，跳过检测");
            }

            firewall = Activator.CreateInstance(type)!;
            dynamic fw = firewall;
            // NET_FW_PROFILE2：1=域网络 2=专用网络 4=公用网络
            var disabled = new List<string>();
            if (!Convert.ToBoolean(fw.FirewallEnabled[1]))
            {
                disabled.Add("域网络");
            }

            if (!Convert.ToBoolean(fw.FirewallEnabled[2]))
            {
                disabled.Add("专用网络");
            }

            if (!Convert.ToBoolean(fw.FirewallEnabled[4]))
            {
                disabled.Add("公用网络");
            }

            return disabled.Count == 0
                ? new CheckItem("firewall", "防火墙", CheckStatus.Ok, "所有网络配置文件的防火墙均已开启")
                : new CheckItem("firewall", "防火墙", CheckStatus.Warn, $"已关闭的防火墙配置文件：{string.Join("、", disabled)}", "建议保持防火墙开启");
        }
        catch (Exception ex)
        {
            return ErrorItem("firewall", "防火墙", ex);
        }
        finally
        {
            if (firewall is not null && Marshal.IsComObject(firewall))
            {
                _ = Marshal.FinalReleaseComObject(firewall);
            }
        }
    });

    private static async Task<CheckItem> CheckProblemDevicesAsync()
    {
        try
        {
            var rows = await QueryWmiAsync(
                "SELECT Name FROM Win32_PnPEntity WHERE ConfigManagerErrorCode IS NOT NULL AND ConfigManagerErrorCode <> 0 AND ConfigManagerErrorCode <> 22").ConfigureAwait(false);

            // 22 = 设备已被禁用，属于用户主动行为，不计入异常
            var names = rows
                .Select(r => r.GetValueOrDefault("Name")?.ToString()?.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();

            return names.Count == 0
                ? new CheckItem("devices", "硬件设备", CheckStatus.Ok, "没有工作异常的设备")
                : new CheckItem("devices", "硬件设备", CheckStatus.Warn,
                    $"{names.Count} 个设备工作异常：{string.Join("、", names.Take(5))}{(names.Count > 5 ? " 等" : "")}",
                    "在设备管理器中检查黄色感叹号设备并安装驱动");
        }
        catch (Exception ex)
        {
            return ErrorItem("devices", "硬件设备", ex);
        }
    }

    private static Task<CheckItem> CheckPendingRebootAsync() => Task.Run(() =>
    {
        try
        {
            return CheckPendingRebootCore();
        }
        catch (Exception ex)
        {
            return ErrorItem("reboot", "待重启", ex);
        }
    });

    private static CheckItem CheckPendingRebootCore()
    {
        var reasons = new List<string>();

        using (var componentReboot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
        {
            if (componentReboot is not null)
            {
                reasons.Add("组件存储");
            }
        }

        using (var updateReboot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
        {
            if (updateReboot is not null)
            {
                reasons.Add("Windows 更新");
            }
        }

        using var sessionManager = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
        var pfro = sessionManager?.GetValue("PendingFileRenameOperations");
        if (PendingFileRename.HasPendingOperations(pfro))
        {
            reasons.Add("文件重命名");
        }

        return reasons.Count == 0
            ? new CheckItem("reboot", "待重启", CheckStatus.Ok, "没有待处理的重启操作")
            : new CheckItem("reboot", "待重启", CheckStatus.Warn, $"有待处理的重启操作（{string.Join("、", reasons)}）", "建议尽快重启以完成系统组件更新");
    }

    private static async Task<CheckItem> CheckNetworkAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await Http
                .GetAsync("https://www.bing.com", HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            return resp.IsSuccessStatusCode
                ? new CheckItem("network", "网络连接", CheckStatus.Ok, "网络连接正常")
                : new CheckItem("network", "网络连接", CheckStatus.Warn, $"外网访问异常（HTTP {(int)resp.StatusCode}）", "检查网络连接或代理设置");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CheckItem("network", "网络连接", CheckStatus.Warn, $"无法访问外网：{ex.Message}", "检查网线 / Wi-Fi 或代理设置");
        }
    }

    private static async Task<CheckItem> CheckPowerPlanAsync(CancellationToken ct)
    {
        try
        {
            var process = await ProcessRunner.RunAsync(
                Path.Combine(Environment.SystemDirectory, "powercfg.exe"),
                ["/getactivescheme"],
                TimeSpan.FromSeconds(10),
                ct).ConfigureAwait(false);
            var output = process.CombinedOutput;
            if (process.ExitCode != 0)
            {
                return ErrorItem("power", "电源计划", new InvalidOperationException($"powercfg 退出码 {process.ExitCode}"));
            }

            var match = Regex.Match(output, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            if (!match.Success)
            {
                return ErrorItem("power", "电源计划", new InvalidOperationException("无法解析 powercfg 输出"));
            }

            var name = match.Value.ToLowerInvariant() switch
            {
                "381b4222-f694-41f0-9685-ff5bb260df2e" => "平衡",
                "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" => "高性能",
                "e9a42b02-d5df-448d-aa00-03f14749eb61" => "卓越性能",
                _ => "自定义",
            };

            return name == "平衡"
                ? new CheckItem("power", "电源计划", CheckStatus.Info, "当前电源计划：平衡", "台式机可切换高性能以发挥全部性能", "optimize")
                : new CheckItem("power", "电源计划", CheckStatus.Ok, $"当前电源计划：{name}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ErrorItem("power", "电源计划", ex);
        }
    }

    private static Task<CheckItem> CheckHiberfilAsync() => Task.Run(() =>
    {
        try
        {
            var sysRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
            var path = Path.Combine(sysRoot, "hiberfil.sys");

            if (!File.Exists(path))
            {
                return new CheckItem("hiberfil", "休眠文件", CheckStatus.Ok, "未启用休眠（无 hiberfil.sys）");
            }

            string size;
            try
            {
                var fi = new FileInfo(path);
                size = SizeFormatter.Format(fi.Length);
            }
            catch
            {
                size = "无法读取（需管理员权限）";
            }

            return new CheckItem("hiberfil", "休眠文件", CheckStatus.Info, $"休眠已启用，hiberfil.sys 占用 {size}", "不需要休眠/快速启动时，可用管理员命令 powercfg /h off 释放该空间");
        }
        catch (Exception ex)
        {
            return ErrorItem("hiberfil", "休眠文件", ex);
        }
    });

    private static Task<CheckItem> CheckSystemDiskAsync() => Task.Run(() =>
    {
        try
        {
            var sys = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\");
            if (!sys.IsReady)
            {
                return new CheckItem("sysdisk", "系统盘空间", CheckStatus.Warn, "系统盘未就绪");
            }

            const long threshold = 20L * 1024 * 1024 * 1024;
            return sys.AvailableFreeSpace < threshold
                ? new CheckItem("sysdisk", "系统盘空间", CheckStatus.Warn, $"系统盘 {sys.Name} 仅剩 {SizeFormatter.Format(sys.AvailableFreeSpace)}", "建议清理大文件，或将下载/常用目录迁移到其他盘", "downloads")
                : new CheckItem("sysdisk", "系统盘空间", CheckStatus.Ok, $"系统盘 {sys.Name} 剩余 {SizeFormatter.Format(sys.AvailableFreeSpace)}");
        }
        catch (Exception ex)
        {
            return ErrorItem("sysdisk", "系统盘空间", ex);
        }
    });

    private static CheckItem ErrorItem(string id, string title, Exception ex)
        => new(id, title, CheckStatus.Warn, $"检测失败：{ex.Message}");

    #endregion

    #region WMI 基础设施

    private static async Task<List<Dictionary<string, object?>>> TryWmiAsync(string query, string? scope = null)
    {
        try
        {
            return await QueryWmiAsync(query, scope).ConfigureAwait(false);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// 执行 WMI 查询并立即物化所有属性值，避免集合释放后访问 COM 对象；
    /// 单项查询超时直接抛 TimeoutException（后台线程自行结束）。
    /// </summary>
    private static async Task<List<Dictionary<string, object?>>> QueryWmiAsync(
        string query,
        string? scope = null,
        TimeSpan? timeout = null)
    {
        var t = Task.Run(() =>
        {
            using var searcher = scope is null
                ? new ManagementObjectSearcher(query)
                : new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query));
            using var results = searcher.Get();
            return results
                .Cast<ManagementBaseObject>()
                .Select(o => o.Properties.Cast<PropertyData>().ToDictionary(p => p.Name, p => (object?)p.Value))
                .ToList();
        });

        var completed = await Task.WhenAny(t, Task.Delay(timeout ?? DefaultWmiTimeout)).ConfigureAwait(false);
        if (completed != t)
        {
            throw new TimeoutException("WMI 查询超时");
        }

        return await t.ConfigureAwait(false);
    }

    #endregion
}
