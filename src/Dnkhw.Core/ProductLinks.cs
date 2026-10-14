namespace Dnkhw.Core;

/// <summary>产品级公开链接的唯一真相源：改官网地址只动这一处。</summary>
public static class ProductLinks
{
    /// <summary>应用内「电脑开荒网」门户。</summary>
    public const string OfficialSite = "https://www.cyhaoka.vip";

    /// <summary>发布与在线更新站点。该站点托管版本清单和不可变安装包。</summary>
    public const string UpdateSite = "https://app.cyhaoka.vip";

    /// <summary>应用内「检查更新」读取的动态版本数据，避免 CDN 缓存旧版清单。</summary>
    public const string VersionJson = UpdateSite + "/dnkhw/version.php";

    /// <summary>官网下载锚点。</summary>
    public const string DownloadPage = UpdateSite + "/#download";
}
