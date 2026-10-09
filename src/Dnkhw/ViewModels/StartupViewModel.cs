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
            UpdateSummary();
        }
        catch (Exception ex)
        {
            NoticeText = $"读取启动项失败：{ex.Message}";
            NoticeOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
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
            row.IsEnabled = !target;
            NoticeText = "该操作需要管理员权限，请点击「以管理员身份重启」后重试";
            NoticeOpen = true;
        }
        catch (Exception ex)
        {
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

    private void UpdateSummary()
    {
        var enabled = Items.Count(i => i.IsEnabled);
        SummaryText = $"共 {Items.Count} 项，启用 {enabled}，已禁用 {Items.Count - enabled}";
    }
}
