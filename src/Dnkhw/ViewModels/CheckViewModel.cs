using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dnkhw.Core.AppCatalog;
using Dnkhw.Core.Downloads;
using Dnkhw.Core.Formatting;
using Dnkhw.Core.Optimize;
using Dnkhw.Core.Startup;
using Dnkhw.Core.SystemCheck;
using Dnkhw.Services;
using Microsoft.Win32;

namespace Dnkhw.ViewModels;

public sealed record OverviewField(string Label, string Value);

public sealed record DiskView(string Root, string Label, string Total, string Free);

/// <summary>开荒进度行：跨模块汇总状态，点击直达对应页面。</summary>
public sealed record ProgressItem(string Label, string StatusText, string PageKey, bool IsActive);

public sealed partial class CheckViewModel : ObservableObject
{
    private readonly SystemCheckService _checkService = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private IReadOnlyList<OverviewField> _fields = [];

    [ObservableProperty]
    private IReadOnlyList<DiskView> _disks = [];

    [ObservableProperty]
    private IReadOnlyList<CheckItem> _results = [];

    [ObservableProperty]
    private bool _noticeOpen;

    [ObservableProperty]
    private string? _noticeText;

    [ObservableProperty]
    private IReadOnlyList<ProgressItem> _progress = [];

    private bool _progressGathered;
    private SystemOverview? Overview { get; set; }

    /// <summary>汇总各模块状态（体检/优化/启动项/迁移/推荐软件），供进度面板展示。</summary>
    [RelayCommand]
    private async Task LoadProgressAsync()
    {
        // 体检一行依赖本页结果（UI 线程状态）；已汇总过时仅原地更新体检行，
        // 不清空其余模块行（避免检查完成时的并发刷新把汇总抹掉）
        var checkRow = new ProgressItem("开荒体检", Results.Count > 0 ? "已完成" : "未开始", "check", Results.Count > 0);
        if (_progressGathered)
        {
            Progress = [checkRow, .. Progress.Where(item => item.PageKey != "check")];
            return;
        }

        _progressGathered = true;
        Progress = [checkRow];
        try
        {
            var rows = await Task.Run(GatherModuleProgress);
            Progress = [checkRow, .. rows];
        }
        catch (Exception ex)
        {
            LogService.Warn("开荒进度汇总失败", ex);
        }
    }

    private static IReadOnlyList<ProgressItem> GatherModuleProgress()
    {
        var rows = new List<ProgressItem>();

        try
        {
            var optimizer = new OptimizeService();
            var items = optimizer.GetItems();
            var applied = items.Count(item => optimizer.IsApplied(item.Id));
            rows.Add(new ProgressItem("Windows 优化", $"{applied}/{items.Count} 已应用", "optimize", applied > 0));
        }
        catch (Exception ex)
        {
            LogService.Warn("进度：优化统计失败", ex);
        }

        try
        {
            var disabled = new StartupService().GetItems().Count(item => !item.IsEnabled);
            rows.Add(new ProgressItem("启动项管理", disabled > 0 ? $"{disabled} 项已禁用" : "暂无禁用项", "startup", disabled > 0));
        }
        catch (Exception ex)
        {
            LogService.Warn("进度：启动项统计失败", ex);
        }

        try
        {
            var migrated = KnownFoldersMigrationService.CountMigratedFolders();
            rows.Add(new ProgressItem("默认路径换盘", $"{migrated}/{KnownFoldersMigrationService.Folders.Count} 已迁移", "downloads", migrated > 0));
        }
        catch (Exception ex)
        {
            LogService.Warn("进度：迁移统计失败", ex);
        }

        try
        {
            var catalog = AppCatalogLoader.LoadDefault();
            var apps = catalog.Categories.SelectMany(category => category.Apps).ToList();
            var installedNames = InstallDetector.GetInstalledDisplayNames();
            var installed = apps.Count(app => InstallDetector.IsInstalled(app.Name, installedNames));
            rows.Add(new ProgressItem("推荐软件", $"{installed}/{apps.Count} 已安装", "apps", installed > 0));
        }
        catch (Exception ex)
        {
            LogService.Warn("进度：推荐软件统计失败", ex);
        }

        return rows;
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        NoticeOpen = false;
        try
        {
            var overviewTask = _checkService.GetOverviewAsync();
            var resultsTask = _checkService.RunChecksAsync();

            var overview = await overviewTask;
            var results = await resultsTask;

            Overview = overview;
            Fields =
            [
                new OverviewField("系统", $"{overview.OsName} {overview.Edition} {overview.DisplayVersion}（{overview.Build}）"),
                new OverviewField("电脑类型", overview.ComputerType),
                new OverviewField("设备型号", overview.SystemModel),
                new OverviewField("主板", overview.BaseBoard),
                new OverviewField("BIOS", overview.BiosInfo),
                new OverviewField("CPU", overview.Cpu),
                new OverviewField("内存", SizeFormatter.Format(overview.RamBytes)),
                new OverviewField("显卡", overview.Gpu),
                new OverviewField("分辨率", overview.Resolution),
                new OverviewField("系统安装日期", overview.InstallDate),
                new OverviewField("主机名", overview.HostName),
                new OverviewField("开机时长", overview.Uptime),
            ];
            Disks = overview.Disks
                .Select(d => new DiskView(d.Root, d.Label, SizeFormatter.Format(d.TotalBytes), SizeFormatter.Format(d.FreeBytes)))
                .ToArray();
            Results = results;
            await LoadProgressAsync();
        }
        catch (Exception ex)
        {
            LogService.Error("开荒检查：系统检查失败", ex);
            NoticeText = $"系统检查失败：{ex.Message}";
            NoticeOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Overview is null || Results.Count == 0)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Markdown 文档|*.md",
            FileName = $"开荒检查报告_{DateTime.Now:yyyyMMdd_HHmm}.md",
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                await File.WriteAllTextAsync(dialog.FileName, CheckReportBuilder.BuildMarkdown(Overview, Results));
                NoticeText = $"已导出报告：{dialog.FileName}";
                NoticeOpen = true;
            }
            catch (Exception ex)
            {
                LogService.Error($"开荒检查：导出报告失败；目标={dialog.FileName}", ex);
                NoticeText = $"导出报告失败：{ex.Message}";
                NoticeOpen = true;
            }
        }
    }

    /// <summary>把 Markdown 报告复制到剪贴板，便于直接粘贴到聊天/笔记。</summary>
    [RelayCommand]
    private void CopyReport()
    {
        if (Overview is null || Results.Count == 0)
        {
            NoticeText = "先点「开始检查」，再复制报告";
            NoticeOpen = true;
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(CheckReportBuilder.BuildMarkdown(Overview, Results));
            NoticeText = "报告已复制到剪贴板，可直接粘贴发送";
            NoticeOpen = true;
        }
        catch (Exception ex)
        {
            LogService.Warn("复制报告到剪贴板失败", ex);
            NoticeText = $"复制失败：{ex.Message}";
            NoticeOpen = true;
        }
    }

    [RelayCommand]
    private void GoFix(string? fixPage)
    {
        switch (fixPage)
        {
            case "downloads":
                AppNavigation.Go<Pages.DefaultPathsPage>();
                break;
            case "optimize":
                AppNavigation.Go<Pages.OptimizePage>();
                break;
            case "apps":
                AppNavigation.Go<Pages.AppsPage>();
                break;
            case "startup":
                AppNavigation.Go<Pages.StartupPage>();
                break;
        }
    }
}
