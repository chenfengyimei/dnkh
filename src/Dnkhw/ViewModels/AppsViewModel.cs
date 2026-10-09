using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dnkhw.Core.AppCatalog;

namespace Dnkhw.ViewModels;

public enum AppRowState
{
    Unknown,
    Installed,
    NotInstalled,
    Installing,
    Succeeded,
    Failed,
}

public sealed partial class AppRowViewModel : ObservableObject
{
    public AppRowViewModel(AppEntry entry) => Entry = entry;

    public AppEntry Entry { get; }

    public string Name => Entry.Name;

    public string Description => Entry.Description;

    public string Homepage => Entry.Homepage;

    public string? WingetId => Entry.WingetId;

    public bool Recommend => Entry.Recommend;

    public bool CanUseWinget => WingetId is not null;

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private AppRowState _state = AppRowState.Unknown;

    [ObservableProperty]
    private string? _statusDetail;

    public string StatusText => State switch
    {
        AppRowState.Installed => "已安装",
        AppRowState.NotInstalled => WingetId is null ? "官网下载" : "可安装",
        AppRowState.Installing => "安装中…",
        AppRowState.Succeeded => "安装成功",
        AppRowState.Failed => "安装失败",
        _ => string.Empty,
    };

    public bool CanInstall => State is AppRowState.NotInstalled && WingetId is not null;
}

public sealed record CategoryViewModel(string Id, string Name, IReadOnlyList<AppRowViewModel> Apps);

public sealed partial class AppsViewModel : ObservableObject
{
    private readonly WingetInstaller _installer = new();

    [ObservableProperty]
    private IReadOnlyList<CategoryViewModel> _categories = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _wingetAvailable;

    [ObservableProperty]
    private bool _noticeOpen;

    [ObservableProperty]
    private string? _noticeText;

    [ObservableProperty]
    private string? _installSummary;

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
            var data = AppCatalogLoader.LoadDefault();
            var installed = await Task.Run(InstallDetector.GetInstalledDisplayNames);
            WingetAvailable = await Task.Run(() => _installer.IsWingetAvailable());

            Categories = data.Categories
                .Select(c => new CategoryViewModel(
                    c.Id,
                    c.Name,
                    c.Apps.Select(a => new AppRowViewModel(a)
                    {
                        State = InstallDetector.IsInstalled(a.Name, installed)
                            ? AppRowState.Installed
                            : AppRowState.NotInstalled,
                    }).ToList()))
                .ToList();

            if (!WingetAvailable)
            {
                NoticeText = "未检测到 winget（App Installer），一键安装不可用；可点右上角「安装 App Installer」后在商店获取";
                NoticeOpen = true;
            }
        }
        catch (Exception ex)
        {
            NoticeText = $"清单加载失败：{ex.Message}";
            NoticeOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task InstallSelectedAsync() => InstallBatchAsync(Categories
        .SelectMany(c => c.Apps)
        .Where(r => r.IsChecked && r.CanInstall)
        .ToList());

    [RelayCommand]
    private Task InstallOneAsync(AppRowViewModel? row)
        => row is null || !row.CanInstall
            ? Task.CompletedTask
            : InstallBatchAsync([row]);

    private async Task InstallBatchAsync(IReadOnlyList<AppRowViewModel> rows)
    {
        if (IsBusy)
        {
            return;
        }

        if (rows.Count == 0)
        {
            NoticeText = "请先勾选「可安装」的应用";
            NoticeOpen = true;
            return;
        }

        IsBusy = true;
        NoticeOpen = false;
        var ok = 0;
        var failed = new List<string>();
        try
        {
            foreach (var (row, index) in rows.Select((r, i) => (r, i)))
            {
                InstallSummary = $"正在安装 {index + 1}/{rows.Count}：{row.Name}";
                row.State = AppRowState.Installing;
                try
                {
                    var result = await _installer.InstallAsync(row.WingetId!);
                    if (result.Success)
                    {
                        row.State = AppRowState.Succeeded;
                        ok++;
                    }
                    else
                    {
                        row.State = AppRowState.Failed;
                        row.StatusDetail = FirstMeaningfulLine(result.Output);
                        failed.Add($"{row.Name}：{row.StatusDetail}");
                    }
                }
                catch (Exception ex)
                {
                    row.State = AppRowState.Failed;
                    row.StatusDetail = ex.Message;
                    failed.Add($"{row.Name}：{ex.Message}");
                }
            }

            InstallSummary = $"批量安装完成：成功 {ok}，失败 {failed.Count}";
            if (failed.Count > 0)
            {
                NoticeText = string.Join(Environment.NewLine, failed.Take(5));
            }
            else
            {
                NoticeText = $"已成功安装 {ok} 个应用";
            }

            NoticeOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenHome(AppRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(row.Homepage) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            NoticeText = $"无法打开官网：{ex.Message}";
            NoticeOpen = true;
        }
    }

    [RelayCommand]
    private void InstallAppInstaller()
    {
        // Microsoft Store 中的 App Installer（winget）产品页
        Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?productid=9NBLGGG16N6W") { UseShellExecute = true });
    }

    private static string FirstMeaningfulLine(string output)
    {
        var line = output
            .Split('\r', '\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('-'));
        return line ?? "未知原因（可重试或改用官网安装）";
    }
}
