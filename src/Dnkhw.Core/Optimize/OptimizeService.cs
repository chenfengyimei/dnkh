using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Dnkhw.Core.Optimize;

/// <summary>
/// Windows 优化项：注册表与服务/电源操作，每项可应用/恢复，完全可逆。
/// 优化目标为关闭推广与广告类功能，不触碰系统核心安全设置。
/// 项按分组组织（广告与推广/搜索体验/通知与建议/隐私与遥测/性能与游戏）。
/// </summary>
public sealed class OptimizeService
{
    private const string HighPerformanceScheme = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string BalancedScheme = "381b4222-f694-41f0-9685-ff5bb260df2e";

    private readonly List<OptimizeAction> _actions = BuildActions();

    private static readonly (string Id, string Name)[] GroupDefinitions =
    [
        ("promo", "广告与推广"),
        ("search", "搜索体验"),
        ("notifications", "通知与建议"),
        ("privacy", "隐私与遥测"),
        ("performance", "性能与游戏"),
    ];

    public bool IsWindows11
    {
        get
        {
            try
            {
                using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                return cv?.GetValue("CurrentBuild") is string build && int.TryParse(build, out var n) && n >= 22000;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>按当前系统版本过滤后的平铺列表（向后兼容）。</summary>
    public IReadOnlyList<OptimizeItem> GetItems()
        => IncludedActions()
            .Select(a => new OptimizeItem(a.Id, a.Title, a.Description, a.NeedsAdmin, a.GroupId, GroupNameOf(a.GroupId)))
            .ToList();

    /// <summary>分组视图：按组归类的优化项（组按固定顺序，空组隐藏）。</summary>
    public IReadOnlyList<OptimizeGroup> GetGroups()
        => GroupDefinitions
            .Select(g => new OptimizeGroup(
                g.Id,
                g.Name,
                IncludedActions()
                    .Where(a => a.GroupId == g.Id)
                    .Select(a => new OptimizeItem(a.Id, a.Title, a.Description, a.NeedsAdmin, g.Id, g.Name))
                    .ToList()))
            .Where(g => g.Items.Count > 0)
            .ToList();

    public bool IsApplied(string id) => Find(id).IsApplied();

    public void Apply(string id)
    {
        var action = Find(id);
        OptimizeBackupStore.SaveIfMissing(id, () => CaptureBackup(action));
        action.Apply();
    }

    public void Revert(string id)
    {
        var action = Find(id);
        if (OptimizeBackupStore.TryGet(id, out var backup))
        {
            RestoreBackup(action, backup);
            OptimizeBackupStore.Remove(id);
            return;
        }

        // 兼容升级前已应用、尚无备份记录的设置：恢复为 Windows 常用默认值。
        action.Revert();
    }

    private IEnumerable<OptimizeAction> IncludedActions()
    {
        var isWin11 = IsWindows11;
        return _actions.Where(a => (!a.Win11Only || isWin11) && (!a.Win10Only || !isWin11));
    }

    private string GroupNameOf(string groupId)
        => GroupDefinitions.FirstOrDefault(g => g.Id == groupId).Name ?? groupId;

    private OptimizeAction Find(string id)
        => _actions.FirstOrDefault(a => a.Id == id) ?? throw new ArgumentOutOfRangeException(nameof(id), $"未知优化项：{id}");

    private sealed record OptimizeAction(
        string Id,
        string Title,
        string Description,
        bool NeedsAdmin,
        Func<bool> IsApplied,
        Action Apply,
        Action Revert,
        bool Win11Only = false,
        bool Win10Only = false,
        string GroupId = "",
        IReadOnlyList<RegistryTarget>? BackupTargets = null,
        bool BackupPowerScheme = false,
        string? ServiceName = null);

    private sealed record RegistryTarget(string Id, RegistryKey Hive, string Path, string Name);

    private static List<OptimizeAction> BuildActions()
    {
        var advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        var contentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        var searchPolicies = @"Software\Policies\Microsoft\Windows\Explorer";

        var actions = new List<OptimizeAction>
        {
            // ---- 广告与推广 ----
            RegAction("startRecommendations", "关闭开始菜单推荐内容",
                "去除 Win11 开始菜单「推荐的项目」中的应用与内容推广",
                advanced, "Start_IrisRecommendations", win11Only: true, group: "promo"),
            RegAction("startSuggestions", "关闭开始菜单建议投放",
                "减少开始菜单里偶尔出现的内容建议",
                contentDelivery, "SubscribedContent-338388Enabled", group: "promo"),
            RegAction("taskbarWidgets", "关闭任务栏小组件 / 资讯和兴趣",
                "移除任务栏左侧的天气资讯与小组件入口",
                advanced, "TaskbarDa", group: "promo"),
            RegAction("lockscreenTips", "关闭锁屏趣味资讯",
                "锁屏壁纸不再展示技巧、提示与广告",
                contentDelivery, "SubscribedContent-310093Enabled", group: "promo"),
            RegAction("scoobe", "关闭「进一步了解 Windows」",
                "抑制登录后弹出的功能推广页",
                @"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", group: "promo"),
            RegAction("silentApps", "关闭静默安装建议应用",
                "阻止 Windows 悄悄安装推广的商店应用",
                contentDelivery, "SilentInstalledAppsEnabled", group: "promo"),

            // ---- 搜索体验 ----
            RegAction("searchHighlights", "关闭搜索框每日热点",
                "去掉 Win11 搜索框里的每日图片与热点资讯",
                @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", win11Only: true, group: "search"),
            new OptimizeAction(
                "searchBoxSuggestions", "禁用搜索框联网搜索建议",
                "开始菜单搜索不再联网取建议（Win11 策略，重启资源管理器或重启电脑后完全生效）",
                NeedsAdmin: false,
                IsApplied: () => GetDword(Registry.CurrentUser, searchPolicies, "DisableSearchBoxSuggestions") == 1,
                Apply: () => SetDword(Registry.CurrentUser, searchPolicies, "DisableSearchBoxSuggestions", 1),
                Revert: () => SetDword(Registry.CurrentUser, searchPolicies, "DisableSearchBoxSuggestions", 0),
                Win11Only: true,
                GroupId: "search",
                BackupTargets: [new RegistryTarget("policy", Registry.CurrentUser, searchPolicies, "DisableSearchBoxSuggestions")]),
            RegAction("searchBing", "关闭搜索框 Bing 网络搜索",
                "Win10：开始菜单搜索不再联网（Win11 上此键无效，已自动隐藏）",
                @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", win10Only: true, group: "search"),

            // ---- 通知与建议 ----
            RegAction("welcomeTips", "关闭「使用技巧和建议」",
                "减少通知中心里的 Windows 使用建议",
                contentDelivery, "SubscribedContent-338389Enabled", group: "notifications"),
            RegAction("notificationSuggestions", "关闭通知中心建议",
                "通知里的产品推荐与建议内容不再弹出",
                contentDelivery, "SubscribedContent-338393Enabled", group: "notifications"),

            // ---- 隐私与遥测 ----
            RegAction("advertisingId", "关闭广告 ID",
                "停止用广告标识符生成个性化广告",
                @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", group: "privacy"),
            new OptimizeAction(
                "telemetry", "限制诊断遥测",
                "限制诊断数据为最低档并停用 DiagTrack 服务（家庭版仍保留基础档；需管理员）",
                NeedsAdmin: true,
                IsApplied: () =>
                    GetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") == 0
                    && GetDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start") == 4,
                Apply: () =>
                {
                    SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0);
                    SetService("DiagTrack", startValue: 4, stop: true);
                },
                Revert: () =>
                {
                    SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 1);
                    SetService("DiagTrack", startValue: 2, stop: false);
                },
                GroupId: "privacy",
                BackupTargets:
                [
                    new RegistryTarget("policy", Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry"),
                    new RegistryTarget("serviceStart", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start"),
                ],
                ServiceName: "DiagTrack"),

            // ---- 性能与游戏 ----
            new OptimizeAction(
                "gameDvr", "关闭 Xbox 后台录制 (Game DVR)",
                "停止游戏后台录制与 Win+G 录制提示，释放部分性能（影响 Xbox 手柄录制按钮）",
                NeedsAdmin: false,
                IsApplied: () =>
                    GetDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled") == 0
                    && GetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled") == 0,
                Apply: () =>
                {
                    SetDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0);
                    SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
                },
                Revert: () =>
                {
                    SetDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 1);
                    SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1);
                },
                GroupId: "performance",
                BackupTargets:
                [
                    new RegistryTarget("gameDvr", Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled"),
                    new RegistryTarget("capture", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled"),
                ]),
            new OptimizeAction(
                "power", "切换高性能电源计划",
                "电源计划切换为高性能，台式机/插电场景更顺手（笔记本略增耗电）",
                NeedsAdmin: false,
                IsApplied: () => string.Equals(GetActivePowerScheme(), HighPerformanceScheme, StringComparison.OrdinalIgnoreCase),
                Apply: () => RunPowerCfg($"/setactive {HighPerformanceScheme}"),
                Revert: () => RunPowerCfg($"/setactive {BalancedScheme}"),
                GroupId: "performance",
                BackupPowerScheme: true),
        };

        return actions;
    }

    private static OptimizeAction RegAction(
        string id,
        string title,
        string description,
        string path,
        string valueName,
        bool needsAdmin = false,
        bool win11Only = false,
        bool win10Only = false,
        string group = "")
        => new(
            id,
            title,
            description,
            needsAdmin,
            IsApplied: () => GetDword(Registry.CurrentUser, path, valueName) == 0,
            Apply: () => SetDword(Registry.CurrentUser, path, valueName, 0),
            Revert: () => SetDword(Registry.CurrentUser, path, valueName, 1),
            Win11Only: win11Only,
            Win10Only: win10Only,
            GroupId: group,
            BackupTargets: [new RegistryTarget("value", Registry.CurrentUser, path, valueName)]);

    private static OptimizeBackup CaptureBackup(OptimizeAction action)
    {
        var registryValues = new Dictionary<string, DwordBackup>(StringComparer.Ordinal);
        foreach (var target in action.BackupTargets ?? [])
        {
            using var key = target.Hive.OpenSubKey(target.Path);
            if (key is null || !key.GetValueNames().Contains(target.Name, StringComparer.OrdinalIgnoreCase))
            {
                registryValues[target.Id] = new DwordBackup(Exists: false, Value: 0);
                continue;
            }

            if (key.GetValueKind(target.Name) != RegistryValueKind.DWord
                || key.GetValue(target.Name) is not int value)
            {
                throw new InvalidOperationException($"无法安全备份注册表值 {target.Path}\\{target.Name}：类型不是 DWORD");
            }

            registryValues[target.Id] = new DwordBackup(Exists: true, value);
        }

        var powerScheme = action.BackupPowerScheme ? GetActivePowerScheme() : null;
        if (action.BackupPowerScheme && string.IsNullOrWhiteSpace(powerScheme))
        {
            throw new InvalidOperationException("无法读取当前电源计划，已取消修改");
        }

        return new OptimizeBackup(
            registryValues,
            powerScheme,
            action.ServiceName is null ? null : TryGetServiceRunning(action.ServiceName));
    }

    private static void RestoreBackup(OptimizeAction action, OptimizeBackup backup)
    {
        foreach (var target in action.BackupTargets ?? [])
        {
            if (!backup.RegistryValues.TryGetValue(target.Id, out var value))
            {
                throw new InvalidOperationException($"优化备份缺少 {target.Id}，为避免错误覆盖已停止恢复");
            }

            if (value.Exists)
            {
                if (!IsSafeBackupValue(action.Id, target.Id, value.Value))
                {
                    throw new InvalidOperationException($"优化备份中的 {target.Id} 值无效，已停止恢复");
                }

                SetDword(target.Hive, target.Path, target.Name, value.Value);
            }
            else
            {
                using var key = target.Hive.OpenSubKey(target.Path, writable: true);
                key?.DeleteValue(target.Name, throwOnMissingValue: false);
            }
        }

        if (action.BackupPowerScheme)
        {
            if (!Guid.TryParse(backup.PowerScheme, out var powerScheme))
            {
                throw new InvalidOperationException("优化备份缺少原电源计划，为避免错误覆盖已停止恢复");
            }

            RunPowerCfg($"/setactive {powerScheme:D}");
        }

        if (action.ServiceName is not null && backup.ServiceRunning is bool wasRunning)
        {
            RunSc($"{(wasRunning ? "start" : "stop")} {action.ServiceName}");
        }
    }

    private static bool? TryGetServiceRunning(string name)
    {
        try
        {
            var result = ProcessRunner.Run(
                Path.Combine(Environment.SystemDirectory, "sc.exe"),
                ["query", name],
                TimeSpan.FromSeconds(10));
            if (result.ExitCode != 0)
            {
                return null;
            }

            return Regex.IsMatch(
                result.CombinedOutput,
                @"(?im)^\s*(?:STATE|状态)\s*:\s*4\b");
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSafeBackupValue(string actionId, string targetId, int value)
    {
        if (actionId == "telemetry" && targetId == "serviceStart")
        {
            return value is 2 or 3 or 4;
        }

        if (actionId == "telemetry" && targetId == "policy")
        {
            return value is >= 0 and <= 3;
        }

        return value is 0 or 1;
    }

    private static int GetDword(RegistryKey hive, string path, string name)
    {
        using var key = hive.OpenSubKey(path);
        return key?.GetValue(name) is int value ? value : -1;
    }

    private static void SetDword(RegistryKey hive, string path, string name, int value)
    {
        using var key = hive.CreateSubKey(path)
            ?? throw new InvalidOperationException($"无法打开注册表 {path}");
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    /// <summary>设置服务启动类型（Start：2=自动 3=手动 4=禁用）并按需停止/启动（sc.exe）。</summary>
    private static void SetService(string name, int startValue, bool stop)
    {
        SetDword(Registry.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\{name}", "Start", startValue);
        try
        {
            RunSc($"{(stop ? "stop" : "start")} {name}");
        }
        catch
        {
            // 服务状态变化失败时仅保留启动类型修改
        }
    }

    private static void RunSc(string arguments)
    {
        _ = ProcessRunner.Run(
            Path.Combine(Environment.SystemDirectory, "sc.exe"),
            arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            TimeSpan.FromSeconds(15));
        // sc stop 已停止的服务等场景会返回非零，忽略
    }

    private static string GetActivePowerScheme()
    {
        var output = RunPowerCfg("/getactivescheme");
        var match = Regex.Match(output, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return match.Success ? match.Value : string.Empty;
    }

    private static string RunPowerCfg(string arguments)
    {
        var result = ProcessRunner.Run(
            Path.Combine(Environment.SystemDirectory, "powercfg.exe"),
            arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            TimeSpan.FromSeconds(10));
        var output = result.CombinedOutput;
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"powercfg 失败（{output.Trim()}）");
        }

        return output;
    }
}
