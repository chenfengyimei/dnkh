using Dnkhw.Core;

namespace Dnkhw.Core.Tests;

public class ProductLinksTests
{
    [Fact]
    public void PortalAndUpdateEndpoints_AreIntentionallySeparated()
    {
        Assert.Equal("https://www.cyhaoka.vip", ProductLinks.OfficialSite);
        Assert.Equal("https://app.cyhaoka.vip", ProductLinks.UpdateSite);
        Assert.Equal("https://app.cyhaoka.vip/dnkhw/version.php", ProductLinks.VersionJson);
        Assert.Equal("https://app.cyhaoka.vip/#download", ProductLinks.DownloadPage);
    }
}
