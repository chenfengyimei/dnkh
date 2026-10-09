using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dnkhw.Core.Formatting;
using Dnkhw.Core.SystemCheck;
using Dnkhw.Services;
using Microsoft.Win32;

namespace Dnkhw.ViewModels;

public sealed record OverviewField(string Label, string Value);

public sealed record DiskView(string Root, string Label, string Total, string Free);

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

    private SystemOverview? Overview { get; set; }

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
                new OverviewField("CPU", overview.Cpu),
                new OverviewField("内存", SizeFormatter.Format(overview.RamBytes)),
                new OverviewField("显卡", overview.Gpu),
                new OverviewField("分辨率", overview.Resolution),
                new OverviewField("主机名", overview.HostName),
                new OverviewField("开机时长", overview.Uptime),
            ];
            Disks = overview.Disks
                .Select(d => new DiskView(d.Root, d.Label, SizeFormatter.Format(d.TotalBytes), SizeFormatter.Format(d.FreeBytes)))
                .ToArray();
            Results = results;
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
            await File.WriteAllTextAsync(dialog.FileName, CheckReportBuilder.BuildMarkdown(Overview, Results));
            NoticeText = $"已导出报告：{dialog.FileName}";
            NoticeOpen = true;
        }
    }

    [RelayCommand]
    private void GoFix(string? fixPage)
    {
        switch (fixPage)
        {
            case "downloads":
                AppNavigation.Go<Pages.DownloadsPage>();
                break;
            case "optimize":
                AppNavigation.Go<Pages.OptimizePage>();
                break;
            case "apps":
                AppNavigation.Go<Pages.AppsPage>();
                break;
        }
    }
}
