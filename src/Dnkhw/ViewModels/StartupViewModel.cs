using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dnkhw.Core.Startup;
using Dnkhw.Services;

namespace Dnkhw.ViewModels;

public sealed partial class StartupItemView : ObservableObject
{
    public StartupItemView(StartupItem item)
    {
        Item = item;
        _isEnabled = item.IsEnabled;
    }

    public StartupItem Item { get; }

    public string Name => Item.Name;

    public string Command => Item.Command;

    public string LocationText => Item.LocationText;

    public bool NeedsAdmin => Item.NeedsAdmin;

    public string PublisherText => Item.Publisher ?? "未签名/未知";

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isBusy;
}

public sealed partial class StartupViewModel : ObservableObject
{
    private readonly StartupService _service = new();

    [ObservableProperty]
    private IReadOnlyList<StartupItemView> _items = [];

    /// <summary>应用搜索词后的可见列表（名称/命令/发布者包含匹配）。</summary>
    [ObservableProperty]
    private IReadOnlyList<StartupItemView> _filteredItems = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>带登录触发器的计划任务（只读展示，不提供开关）。</summary>
    [ObservableProperty]
    private IReadOnlyList<ScheduledTaskInfo> _tasks = [];

    /// <summary>自启动系统服务（只读展示，不提供开关）。</summary>
    [ObservableProperty]
    private IReadOnlyList<ServiceStartupInfo> _services = [];

    [ObservableProperty]
    private string _taskSummaryText = string.Empty;

    [ObservableProperty]
    private string _serviceSummaryText = string.Empty;

    [ObservableProperty]
    private bool _isAdmin = AdminRelauncher.IsAdmin();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _noticeOpen;

    [ObservableProperty]
    private string? _noticeText;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        NoticeOpen = false;
        try
        {
            var items = await Task.Run(() => _service.GetItems());
            Items = items.Select(i => new StartupItemView(i)).ToList();
            ApplyFilter();
            UpdateSummary();
        }
        catch (Exception ex)
        {
            LogService.Error("启动项管理：读取启动项失败", ex);
            NoticeText = $"读取启动项失败：{ex.Message}";
            NoticeOpen = true;
        }
        finally
        {
            IsBusy = false;
        }

        // 计划任务与系统服务：只读补充视图，失败不影响主列表
        try
        {
            var tasks = await Task.Run(ScheduledTaskService.GetLogonTriggeredTasks);
            Tasks = tasks;
            TaskSummaryText = $"与任务管理器「启动应用」页同类，共 {tasks.Count} 个";
        }
        catch (Exception ex)
        {
            LogService.Warn("读取登录触发计划任务失败", ex);
            TaskSummaryText = "计划任务读取失败（不影响上方启动项管理）";
        }

        try
        {
            var services = await Task.Run(ServiceStartupService.GetAutoStartServices);
            Services = services;
            ServiceSummaryText = $"开机自启动的系统服务，共 {services.Count} 个";
        }
        catch (Exception ex)
        {
            LogService.Warn("读取自启动服务失败", ex);
            ServiceSummaryText = "服务读取失败（不影响上方启动项管理）";
        }
    }

    private void ApplyFilter()
    {
        var term = SearchText?.Trim();
        FilteredItems = string.IsNullOrEmpty(term)
            ? Items
            : Items.Where(i =>
                i.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || i.Command.Contains(term, StringComparison.OrdinalIgnoreCase)
                || i.PublisherText.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>由页面的开关 Click 调用；开关是 TwoWay 绑定，此刻已是目标值，失败时回拨。</summary>
    public async Task ToggleAsync(StartupItemView row)
    {
        if (row.IsBusy)
        {
            return;
        }

        row.IsBusy = true;
        NoticeOpen = false;
        var target = row.IsEnabled;
        try
        {
            await Task.Run(() => _service.SetEnabled(row.Item, target));
            UpdateSummary();
        }
        catch (UnauthorizedAccessException)
        {
            LogService.Warn($"启动项管理：权限不足；项目={row.Name}；目标状态={target}");
            row.IsEnabled = !target;
            NoticeText = "该操作需要管理员权限，请点击「以管理员身份重启」后重试";
            NoticeOpen = true;
        }
        catch (Exception ex)
        {
            LogService.Error($"启动项管理：切换失败；项目={row.Name}；目标状态={target}", ex);
            row.IsEnabled = !target;
            NoticeText = $"操作失败：{ex.Message}";
            NoticeOpen = true;
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    [RelayCommand]
    private void RelaunchAdmin() => AdminRelauncher.Relaunch();

    /// <summary>打开系统「启动应用」设置页（禁用计划任务类启动项请在此操作）。</summary>
    [RelayCommand]
    private void OpenStartupSettings() => OpenExternal("ms-settings:startupapps", "启动应用设置");

    /// <summary>打开服务管理控制台（services.msc）。</summary>
    [RelayCommand]
    private void OpenServicesConsole() => OpenExternal("services.msc", "服务控制台");

    private void OpenExternal(string target, string displayName)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogService.Warn($"打开 {displayName} 失败", ex);
            NoticeText = $"无法打开 {displayName}：{ex.Message}";
            NoticeOpen = true;
        }
    }

    private void UpdateSummary()
    {
        var enabled = Items.Count(i => i.IsEnabled);
        SummaryText = $"共 {Items.Count} 项，启用 {enabled}，已禁用 {Items.Count - enabled}";
    }
}
