using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dnkhw.Core.Optimize;
using Dnkhw.Services;

namespace Dnkhw.ViewModels;

public sealed partial class OptimizeRowView : ObservableObject
{
    public OptimizeRowView(OptimizeItem item, bool isApplied)
    {
        Item = item;
        _isApplied = isApplied;
    }

    public OptimizeItem Item { get; }

    public string Title => Item.Title;

    public string Description => Item.Description;

    public bool NeedsAdmin => Item.NeedsAdmin;

    [ObservableProperty]
    private bool _isApplied;

    [ObservableProperty]
    private bool _isBusy;
}

public sealed partial class OptimizeGroupView : ObservableObject
{
    public OptimizeGroupView(string id, string name, IReadOnlyList<OptimizeRowView> rows)
    {
        Id = id;
        Name = name;
        Rows = rows;
    }

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyList<OptimizeRowView> Rows { get; }

    [ObservableProperty]
    private string _progressText = string.Empty;

    public void RefreshProgress()
    {
        var applied = Rows.Count(r => r.IsApplied);
        ProgressText = $"已应用 {applied}/{Rows.Count}";
    }
}

public sealed partial class OptimizeViewModel : ObservableObject
{
    private readonly OptimizeService _service = new();

    [ObservableProperty]
    private IReadOnlyList<OptimizeGroupView> _groups = [];

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
            var groups = await Task.Run(() => _service.GetGroups()
                .Select(g => new OptimizeGroupView(
                    g.Id,
                    g.Name,
                    g.Items.Select(i => new OptimizeRowView(i, _service.IsApplied(i.Id))).ToList()))
                .ToList());
            foreach (var g in groups)
            {
                g.RefreshProgress();
            }

            Groups = groups;
            UpdateSummary();
        }
        catch (Exception ex)
        {
            NoticeText = $"读取优化项失败：{ex.Message}";
            NoticeOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>开关 Click 调用：此刻 IsApplied 已是目标值，失败时回拨。</summary>
    public async Task ToggleAsync(OptimizeRowView row)
    {
        if (row.IsBusy)
        {
            return;
        }

        row.IsBusy = true;
        NoticeOpen = false;
        var target = row.IsApplied;
        try
        {
            await Task.Run(() =>
            {
                if (target)
                {
                    _service.Apply(row.Item.Id);
                }
                else
                {
                    _service.Revert(row.Item.Id);
                }
            });
            UpdateSummary();
        }
        catch (UnauthorizedAccessException)
        {
            row.IsApplied = !target;
            NoticeText = "该操作需要管理员权限，请点击「以管理员身份重启」后重试";
            NoticeOpen = true;
        }
        catch (Exception ex)
        {
            row.IsApplied = !target;
            NoticeText = $"操作失败：{ex.Message}";
            NoticeOpen = true;
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    /// <summary>组内一键应用：该组未应用的项目全部应用。</summary>
    [RelayCommand]
    private async Task ApplyGroupAsync(OptimizeGroupView? group)
    {
        if (group is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        NoticeOpen = false;
        var applied = 0;
        var failed = new List<string>();
        try
        {
            foreach (var row in group.Rows.Where(r => !r.IsApplied))
            {
                row.IsBusy = true;
                try
                {
                    await Task.Run(() => _service.Apply(row.Item.Id));
                    row.IsApplied = true;
                    applied++;
                }
                catch (Exception ex)
                {
                    failed.Add($"{row.Title}：{ex.Message}");
                }
                finally
                {
                    row.IsBusy = false;
                }
            }

            NoticeText = failed.Count == 0
                ? $"「{group.Name}」已应用 {applied} 项"
                : $"应用 {applied} 项，失败 {failed.Count} 项：{string.Join("；", failed.Take(3))}";
            NoticeOpen = true;
            UpdateSummary();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyAllAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var targets = Groups.SelectMany(g => g.Rows).Where(r => !r.IsApplied && (IsAdmin || !r.NeedsAdmin)).ToList();
        if (targets.Count == 0)
        {
            NoticeText = "没有可优化的项目（已全部应用或需提权的未勾选）";
            NoticeOpen = true;
            return;
        }

        IsBusy = true;
        NoticeOpen = false;
        var applied = 0;
        var failed = new List<string>();
        try
        {
            foreach (var row in targets)
            {
                row.IsBusy = true;
                try
                {
                    await Task.Run(() => _service.Apply(row.Item.Id));
                    row.IsApplied = true;
                    applied++;
                }
                catch (Exception ex)
                {
                    failed.Add($"{row.Title}：{ex.Message}");
                }
                finally
                {
                    row.IsBusy = false;
                }
            }

            NoticeText = failed.Count == 0
                ? $"一键优化完成：应用 {applied} 项"
                : $"应用 {applied} 项，失败 {failed.Count} 项：{string.Join("；", failed.Take(3))}";
            NoticeOpen = true;
            UpdateSummary();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void RelaunchAdmin() => AdminRelauncher.Relaunch();

    private void UpdateSummary()
    {
        var rows = Groups.SelectMany(g => g.Rows).ToList();
        var applied = rows.Count(i => i.IsApplied);
        SummaryText = $"可优化 {rows.Count} 项，已应用 {applied} 项";
        foreach (var group in Groups)
        {
            group.RefreshProgress();
        }
    }
}
