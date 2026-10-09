using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dnkhw.Core.Wallpaper;
using Microsoft.Win32;

namespace Dnkhw.ViewModels;

public sealed record FitOption(string Label, WallpaperFit Fit);

public sealed record WallpaperView(
    string Title,
    string DateText,
    string? ThumbPath,
    string? FullPath,
    bool IsDownloaded,
    bool IsLocal,
    BingWallpaper? Source);

public sealed partial class WallpaperViewModel : ObservableObject
{
    private readonly BingWallpaperService _bing = new();
    private readonly WallpaperStore _store = new();

    public IReadOnlyList<FitOption> FitOptions { get; } =
    [
        new FitOption("填充", WallpaperFit.Fill),
        new FitOption("适应", WallpaperFit.Fit),
        new FitOption("拉伸", WallpaperFit.Stretch),
        new FitOption("平铺", WallpaperFit.Tile),
        new FitOption("居中", WallpaperFit.Center),
    ];

    [ObservableProperty]
    private FitOption? _selectedFit;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private IReadOnlyList<WallpaperView> _items = [];

    [ObservableProperty]
    private bool _noticeOpen;

    [ObservableProperty]
    private string? _noticeText;

    public WallpaperViewModel()
    {
        _selectedFit = FitOptions[0];
    }

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
            IReadOnlyList<BingWallpaper> list;
            try
            {
                list = await _bing.GetRecentAsync(8);
            }
            catch (Exception ex)
            {
                list = [];
                NoticeText = $"Bing 接口不可用（{ex.Message}），仅显示本地缓存";
                NoticeOpen = true;
            }

            var items = new List<WallpaperView>();
            var recentFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var w in list)
            {
                string? thumbPath = null;
                try
                {
                    thumbPath = await _store.DownloadThumbAsync(w);
                }
                catch
                {
                    // 缩略图下载失败仅影响预览
                }

                var fullPath = _store.GetFullPath(w.FullFileName);
                recentFiles.Add(Path.GetFileName(fullPath));
                items.Add(new WallpaperView(
                    w.Title,
                    FormatDate(w.EndDate),
                    thumbPath,
                    fullPath,
                    File.Exists(fullPath),
                    IsLocal: false,
                    Source: w));
            }

            // 附加本地缓存中不在最近一周列表内的文件（离线可用）
            foreach (var file in _store.GetCachedFiles())
            {
                if (!recentFiles.Contains(Path.GetFileName(file)))
                {
                    items.Add(new WallpaperView(
                        Path.GetFileNameWithoutExtension(file),
                        string.Empty,
                        file,
                        file,
                        IsDownloaded: true,
                        IsLocal: true,
                        Source: null));
                }
            }

            Items = items;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetWallpaperAsync(WallpaperView? view)
    {
        if (view is null)
        {
            return;
        }

        IsBusy = true;
        NoticeOpen = false;
        try
        {
            string path;
            if (view.IsDownloaded && view.FullPath is not null)
            {
                path = view.FullPath;
            }
            else if (view.Source is not null)
            {
                path = await _store.DownloadAsync(view.Source.FullUrl, view.Source.FullFileName);
                Items = Items.Select(i => ReferenceEquals(i, view)
                    ? i with { IsDownloaded = true, FullPath = path }
                    : i).ToArray();
            }
            else
            {
                return;
            }

            WallpaperService.Set(path, SelectedFit?.Fit ?? WallpaperFit.Fill);
            NoticeText = $"已设置壁纸：{view.Title}";
            NoticeOpen = true;
        }
        catch (Exception ex)
        {
            NoticeText = $"设置失败：{ex.Message}";
            NoticeOpen = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SetLocal()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp",
            Title = "选择壁纸图片",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            WallpaperService.Set(dialog.FileName, SelectedFit?.Fit ?? WallpaperFit.Fill);
            NoticeText = $"已设置壁纸：{Path.GetFileName(dialog.FileName)}";
            NoticeOpen = true;
        }
        catch (Exception ex)
        {
            NoticeText = $"设置失败：{ex.Message}";
            NoticeOpen = true;
        }
    }

    private static string FormatDate(string endDate)
        => DateTime.TryParseExact(endDate, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d)
            ? $"{d.Month}月{d.Day}日 {d:ddd}"
            : endDate;
}
