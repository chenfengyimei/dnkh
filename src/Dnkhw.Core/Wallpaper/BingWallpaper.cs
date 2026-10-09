namespace Dnkhw.Core.Wallpaper;

/// <summary>
/// Bing 每日壁纸条目。
/// </summary>
/// <param name="Id">唯一标识（如 OHR.FrostAmaranth_ZH-CN0424）。</param>
public sealed record BingWallpaper(
    string Id,
    string Title,
    string Copyright,
    string EndDate,
    string ThumbUrl,
    string FullUrl,
    string ThumbFileName,
    string FullFileName);
