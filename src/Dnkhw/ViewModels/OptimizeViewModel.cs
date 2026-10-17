using System.Diagnostics;
using System.IO;
using System.Threading;
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

    /// <summary>经典右键菜单项当前是否已应用（决定「重启资源管理器」按钮是否显示）。</summary>
    [ObservableProperty]
    private bool _isClassicMenuApplied;

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
            LogService.Error("Windows 优化：读取优化项状态失败", ex);
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
            LogService.Warn($"Windows 优化：权限不足；项目={row.Item.Id}；目标状态={target}");
            row.IsApplied = !target;
            NoticeText = "该操作需要管理员权限，请点击「以管理员身份重启」后重试";
            NoticeOpen = true;
        }
        catch (Exception ex)
        {
            LogService.Error($"Windows 优化：切换项目失败；项目={row.Item.Id}；目标状态={target}", ex);
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
                    LogService.Error($"Windows 优化：分组应用失败；分组={group.Name}；项目={row.Item.Id}", ex);
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
                    LogService.Error($"Windows 优化：全部应用失败；项目={row.Item.Id}", ex);
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

    /// <summary>全部恢复：把本工具应用过的优化项逐项还原（含无备份记录的旧项，走默认值恢复）。</summary>
    [RelayCommand]
    private async Task RestoreAllAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var targets = Groups.SelectMany(g => g.Rows).Where(r => r.IsApplied).ToList();
        if (targets.Count == 0)
        {
            NoticeText = "当前没有已应用的优化项，无需恢复";
            NoticeOpen = true;
            return;
        }

        IsBusy = true;
        NoticeOpen = false;
        var restored = 0;
        var failed = new List<string>();
        try
        {
            foreach (var row in targets)
            {
                row.IsBusy = true;
                try
                {
                    await Task.Run(() => _service.Revert(row.Item.Id));
                    row.IsApplied = false;
                    restored++;
                }
                catch (Exception ex)
                {
                    LogService.Error($"Windows 优化：全部恢复失败；项目={row.Item.Id}", ex);
                    failed.Add($"{row.Title}：{ex.Message}");
                }
                finally
                {
                    row.IsBusy = false;
                }
            }

            NoticeText = failed.Count == 0
                ? $"已全部恢复：还原 {restored} 项"
                : $"还原 {restored} 项，失败 {failed.Count} 项：{string.Join("；", failed.Take(3))}";
            NoticeOpen = true;
            UpdateSummary();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>经典右键菜单写入后需重启资源管理器生效；弹确认再执行。</summary>
    [RelayCommand]
    private void RestartExplorer()
    {
        var owner = System.Windows.Application.Current.MainWindow;
        var answer = System.Windows.MessageBox.Show(
            "重启资源管理器会让任务栏与已打开的文件夹窗口短暂消失（约 1 秒）。\n其他程序不受影响。现在重启吗？",
            "电脑开荒",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            foreach (var process in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // 个别进程退出中的竞态可忽略
                }
            }

            Thread.Sleep(700);
            Process.Start(new ProcessStartInfo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "explorer.exe"))
            {
                UseShellExecute = true,
            });
            NoticeText = "资源管理器已重启，右键菜单设置已生效";
            NoticeOpen = true;
        }
        catch (Exception ex)
        {
            LogService.Warn("重启资源管理器失败", ex);
            NoticeText = $"重启资源管理器失败：{ex.Message}（可手动重启电脑生效）";
            NoticeOpen = true;
        }
    }

    [RelayCommand]
    private void RelaunchAdmin() => AdminRelauncher.Relaunch();

    private void UpdateSummary()
    {
        var rows = Groups.SelectMany(g => g.Rows).ToList();
        var applied = rows.Count(i => i.IsApplied);
        SummaryText = $"可优化 {rows.Count} 项，已应用 {applied} 项";
        IsClassicMenuApplied = rows.FirstOrDefault(r => r.Item.Id == "classicContextMenu")?.IsApplied ?? false;
        foreach (var group in Groups)
        {
            group.RefreshProgress();
        }
    }
}
