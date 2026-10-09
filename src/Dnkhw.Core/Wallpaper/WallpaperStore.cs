using System.Net.Http;

namespace Dnkhw.Core.Wallpaper;

/// <summary>
/// 壁纸本地仓库：统一管理「图片\dnkhw\Wallpapers」目录的下载与缓存。
/// </summary>
public sealed class WallpaperStore
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public string Directory { get; }

    public WallpaperStore(string? directory = null)
    {
        Directory = directory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "dnkhw", "Wallpapers");
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string GetFullPath(string fileName) => Path.Combine(Directory, fileName);

    public async Task<string> DownloadAsync(string url, string fileName, CancellationToken ct = default)
    {
        var target = GetFullPath(fileName);
        if (File.Exists(target))
        {
            return target;
        }

        // 先写临时文件再落盘，避免中断留下半张图片
        var temp = target + ".part";
        using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            await using var source = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(file, ct).ConfigureAwait(false);
        }

        File.Move(temp, target, overwrite: true);
        return target;
    }

    public Task<string> DownloadThumbAsync(BingWallpaper wallpaper, CancellationToken ct = default)
        => DownloadAsync(wallpaper.ThumbUrl, wallpaper.ThumbFileName, ct);

    /// <summary>列出已缓存壁纸文件（按修改时间倒序）。</summary>
    public IReadOnlyList<string> GetCachedFiles()
    {
        return [.. System.IO.Directory
            .EnumerateFiles(Directory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))];
    }
}
