using System.Net.Http;

namespace Dnkhw.Core.Wallpaper;

/// <summary>
/// 壁纸本地仓库：统一管理「图片\dnkhw\Wallpapers」目录的下载与缓存。
/// </summary>
public sealed class WallpaperStore
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private const long MaxDownloadBytes = 50L * 1024 * 1024;
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp",
    };

    public string Directory { get; }

    public WallpaperStore(string? directory = null)
    {
        Directory = Path.GetFullPath(directory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "dnkhw", "Wallpapers"));
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string GetFullPath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
            || fileName is "." or ".."
            || !SupportedExtensions.Contains(Path.GetExtension(fileName)))
        {
            throw new ArgumentException("壁纸文件名无效", nameof(fileName));
        }

        var target = Path.GetFullPath(Path.Combine(Directory, fileName));
        var directoryPrefix = Path.TrimEndingDirectorySeparator(Directory) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("壁纸文件必须位于缓存目录内", nameof(fileName));
        }

        return target;
    }

    public async Task<string> DownloadAsync(string url, string fileName, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "www.bing.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("仅允许从 Bing HTTPS 地址下载壁纸", nameof(url));
        }

        var target = GetFullPath(fileName);
        if (File.Exists(target) && new FileInfo(target).Length > 0)
        {
            return target;
        }

        // 先写临时文件再落盘，避免中断留下半张图片
        var temp = Path.Combine(Directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.part");
        try
        {
            using (var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                var mediaType = resp.Content.Headers.ContentType?.MediaType;
                if (mediaType is null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("服务器返回的内容不是图片");
                }

                if (resp.Content.Headers.ContentLength is > MaxDownloadBytes)
                {
                    throw new InvalidDataException("壁纸文件超过 50 MB 安全上限");
                }

                await using var source = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaxDownloadBytes)
                    {
                        throw new InvalidDataException("壁纸文件超过 50 MB 安全上限");
                    }

                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }

                if (total == 0)
                {
                    throw new InvalidDataException("服务器返回了空图片");
                }
            }

            File.Move(temp, target, overwrite: true);
            return target;
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // 下载失败后的临时文件会在下次缓存枚举时忽略。
            }
        }
    }

    public Task<string> DownloadThumbAsync(BingWallpaper wallpaper, CancellationToken ct = default)
        => DownloadAsync(wallpaper.ThumbUrl, wallpaper.ThumbFileName, ct);

    /// <summary>列出已缓存壁纸文件（按修改时间倒序）。</summary>
    public IReadOnlyList<string> GetCachedFiles()
    {
        return [.. System.IO.Directory
            .EnumerateFiles(Directory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))];
    }
}
