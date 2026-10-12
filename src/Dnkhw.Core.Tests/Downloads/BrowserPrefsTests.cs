using Dnkhw.Core.Downloads;

namespace Dnkhw.Core.Tests.Downloads;

public class BrowserPrefsTests
{
    [Fact]
    public void ChromiumPrefs_ParsesDefaultDirectory()
    {
        const string json = """{"download":{"default_directory":"D:\\Downloads"},"other":1}""";
        Assert.Equal(@"D:\Downloads", ChromiumPrefs.ParseDefaultDirectory(json));
    }

    [Fact]
    public void ChromiumPrefs_EmptyOrNull_ReturnsNull()
    {
        Assert.Null(ChromiumPrefs.ParseDefaultDirectory("""{"download":{"default_directory":""}}"""));
        Assert.Null(ChromiumPrefs.ParseDefaultDirectory("""{"download":{}}"""));
        Assert.Null(ChromiumPrefs.ParseDefaultDirectory("""{}"""));
    }

    [Fact]
    public void FirefoxPrefs_CustomFolderList2_ReturnsDir()
    {
        const string prefs = """
            user_pref("browser.download.useDownloadDir", true);
            user_pref("browser.download.folderList", 2);
            user_pref("browser.download.dir", "D:\\我的 下载");
            """;
        Assert.Equal(@"D:\我的 下载", FirefoxPrefs.ParseDownloadDir(prefs));
    }

    [Fact]
    public void FirefoxPrefs_FollowsSystemFolderList1_ReturnsNull()
    {
        const string prefs = """
            user_pref("browser.download.folderList", 1);
            user_pref("browser.download.dir", "D:\\ignored");
            """;
        Assert.Null(FirefoxPrefs.ParseDownloadDir(prefs));
    }

    [Fact]
    public void FirefoxPrefs_DesktopFolderList0_ReturnsDesktop()
    {
        const string prefs = """user_pref("browser.download.folderList", 0);""";

        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            FirefoxPrefs.ParseDownloadDir(prefs));
    }

    [Fact]
    public void FirefoxPrefs_LastPreferenceWins()
    {
        const string prefs = """
            user_pref("browser.download.folderList", 1);
            user_pref("browser.download.folderList", 2);
            user_pref("browser.download.dir", "C:\\old");
            user_pref("browser.download.dir", "D:\\new");
            """;

        Assert.Equal(@"D:\new", FirefoxPrefs.ParseDownloadDir(prefs));
    }
}
