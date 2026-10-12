namespace Dnkhw.Core;

/// <summary>产品级公开链接的唯一真相源：改官网地址只动这一处。</summary>
public static class ProductLinks
{
    public const string OfficialSite = "https://www.cyhaoka.vip";

    /// <summary>应用内「检查更新」读取的版本数据（需部署到官网）。</summary>
    public const string VersionJson = OfficialSite + "/dnkhw/version.json";

    /// <summary>官网下载锚点。</summary>
    public const string DownloadPage = OfficialSite + "/#download";
}
