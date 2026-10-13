using System.Net.Http;

namespace Dnkhw.Core.Wallpaper;

/// <summary>
/// 壁纸本地仓库：统一管理「图片\dnkhw\Wallpapers」目录的下载与缓存。
/// </summary>
public sealed class WallpaperStore
{
    private const long MaxDownloadBytes = 50L * 1024 * 1024;
    private const int MaxRedirects = 5;
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp",
    };

    private readonly HttpClient _http;

    public string Directory { get; }

    public WallpaperStore(string? directory = null, HttpMessageHandler? handler = null)
    {
        _http = handler is null
            ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) }
            : new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(60) };
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
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsAllowedBingUri(uri))
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
            using (var resp = await GetAllowedResponseAsync(uri, ct).ConfigureAwait(false))
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

            if (!HasSupportedImageSignature(temp))
            {
                throw new InvalidDataException("下载内容不是受支持的图片格式");
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

    /// <summary>只接受 Bing 自有域名；重定向逐跳检查，避免借图片下载请求访问任意站点。</summary>
    internal static bool IsAllowedBingUri(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host;
        return host.Equals("bing.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".bing.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("bing.net", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".bing.net", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpResponseMessage> GetAllowedResponseAsync(Uri initialUri, CancellationToken ct)
    {
        var current = initialUri;
        for (var redirectCount = 0; redirectCount <= MaxRedirects; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var statusCode = (int)response.StatusCode;
            if (statusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null)
                {
                    throw new HttpRequestException("壁纸下载重定向缺少目标地址");
                }

                var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (!IsAllowedBingUri(next))
                {
                    throw new InvalidDataException("壁纸下载重定向到了非 Bing 地址，已拒绝");
                }

                current = next;
                continue;
            }

            if (!IsAllowedBingUri(response.RequestMessage?.RequestUri ?? current))
            {
                response.Dispose();
                throw new InvalidDataException("壁纸响应并非来自受信任的 Bing 地址");
            }

            return response;
        }

        throw new HttpRequestException($"壁纸下载重定向超过 {MaxRedirects} 次");
    }

    private static bool HasSupportedImageSignature(string path)
    {
        Span<byte> header = stackalloc byte[8];
        using var stream = File.OpenRead(path);
        var read = stream.Read(header);
        return read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF // JPEG
            || read >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) // PNG
            || read >= 2 && header[0] == (byte)'B' && header[1] == (byte)'M'; // BMP
    }

    /// <summary>列出已缓存壁纸文件（按修改时间倒序）。</summary>
    public IReadOnlyList<string> GetCachedFiles()
    {
        return [.. System.IO.Directory
            .EnumerateFiles(Directory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))];
    }
}
