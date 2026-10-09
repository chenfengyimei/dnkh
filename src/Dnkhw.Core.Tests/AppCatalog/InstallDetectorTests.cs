using Dnkhw.Core.AppCatalog;

namespace Dnkhw.Core.Tests.AppCatalog;

public class InstallDetectorTests
{
    [Theory]
    [InlineData("7-Zip", "7-Zip 25.02 (x64)")]
    [InlineData("Google Chrome", "Chrome")]
    [InlineData("VLC", "VLC media player")]
    [InlineData("微信", "微信 (WeChat)")]
    public void IsInstalled_MatchesVariants(string appName, string installedName)
    {
        Assert.True(InstallDetector.IsInstalled(appName, [installedName]));
    }

    [Theory]
    [InlineData("7-Zip", "WinRAR 压缩工具")]
    [InlineData("VLC", "Video Player Pro")]
    [InlineData("Everything", "Everyding 搜索")]
    public void IsInstalled_DoesNotMatchDifferentApps(string appName, string installedName)
    {
        Assert.False(InstallDetector.IsInstalled(appName, [installedName]));
    }

    [Fact]
    public void IsInstalled_EmptyList_False()
    {
        Assert.False(InstallDetector.IsInstalled("7-Zip", Array.Empty<string>()));
    }
}
