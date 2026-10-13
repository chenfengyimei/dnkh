namespace Dnkhw.Core;

/// <summary>产品级公开链接的唯一真相源：改官网地址只动这一处。</summary>
public static class ProductLinks
{
    public const string OfficialSite = "https://app.cyhaoka.vip";

    /// <summary>应用内「检查更新」读取的动态版本数据，避免 CDN 缓存旧版清单。</summary>
    public const string VersionJson = OfficialSite + "/dnkhw/version.php";

    /// <summary>官网下载锚点。</summary>
    public const string DownloadPage = OfficialSite + "/#download";
}
