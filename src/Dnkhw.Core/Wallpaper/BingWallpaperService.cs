using System.Net.Http;
using System.Text.Json;

namespace Dnkhw.Core.Wallpaper;

/// <summary>
/// Bing 中国区每日壁纸接口（HPImageArchive，无需 API Key）。
/// </summary>
public sealed class BingWallpaperService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static string BuildFullUrl(string urlBase) => $"https://www.bing.com{NormalizeBase(urlBase)}_1920x1080.jpg";

    public static string BuildThumbUrl(string urlBase) => $"https://www.bing.com{NormalizeBase(urlBase)}_400x240.jpg";

    private static string NormalizeBase(string urlBase)
        => urlBase.StartsWith('/') ? urlBase : "/" + urlBase;

    /// <summary>
    /// 从 urlbase（形如 /th?id=OHR.FrostAmaranth_ZH-CN0424）提取可直接用作文件名的 Id。
    /// </summary>
    public static string ExtractId(string urlBase)
    {
        var idx = urlBase.LastIndexOf("id=", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return urlBase[(idx + 3)..];
        }

        // 旧格式 /az/hprichbg/RB.xxx 的兜底
        return Path.GetFileName(urlBase);
    }

    /// <summary>从 copyright 字段提取主标题（截取首个全角/半角括号前的文本）。</summary>
    public static string ExtractTitle(string copyright)
    {
        var index = copyright.IndexOf('（');
        if (index < 0)
        {
            index = copyright.IndexOf('(');
        }

        return index > 0 ? copyright[..index] : copyright;
    }

    /// <summary>
    /// 获取最近 n 天的壁纸列表（Bing 最多保留 8 天）。
    /// </summary>
    public async Task<IReadOnlyList<BingWallpaper>> GetRecentAsync(int count = 8, CancellationToken ct = default)
    {
        var url = $"https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n={count}&mkt=zh-CN";
        using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("images", out var images))
        {
            return [];
        }

        var result = new List<BingWallpaper>();
        foreach (var img in images.EnumerateArray())
        {
            var urlBase = img.GetProperty("urlbase").GetString();
            var copyright = img.GetProperty("copyright").GetString();
            var endDate = img.GetProperty("enddate").GetString();
            if (string.IsNullOrWhiteSpace(urlBase) || copyright is null || endDate is null)
            {
                continue;
            }

            var id = ExtractId(urlBase);
            result.Add(new BingWallpaper(
                Id: id,
                Title: ExtractTitle(copyright),
                Copyright: copyright,
                EndDate: endDate,
                ThumbUrl: BuildThumbUrl(urlBase),
                FullUrl: BuildFullUrl(urlBase),
                ThumbFileName: $"{id}_400x240.jpg",
                FullFileName: $"{id}_1920x1080.jpg"));
        }

        return result;
    }
}
