using Dnkhw.Core.Wallpaper;

namespace Dnkhw.Core.Tests.Wallpaper;

public class BingWallpaperServiceTests
{
    [Theory]
    [InlineData("OHR.FrostAmaranth_ZH-CN0424", "https://www.bing.com/OHR.FrostAmaranth_ZH-CN0424_1920x1080.jpg")]
    [InlineData("/th?id=OHR.Abc", "https://www.bing.com/th?id=OHR.Abc_1920x1080.jpg")]
    public void BuildFullUrl_MatchesExpected(string urlBase, string expected)
    {
        Assert.Equal(expected, BingWallpaperService.BuildFullUrl(urlBase));
    }

    [Theory]
    [InlineData("/th?id=OHR.FrostAmaranth_ZH-CN0424", "OHR.FrostAmaranth_ZH-CN0424")]
    [InlineData("/th?id=OHR.Abc_ZH-CN123", "OHR.Abc_ZH-CN123")]
    [InlineData("/az/hprichbg/RB.Legacy", "RB.Legacy")]
    public void ExtractId_ProducesValidFileName(string urlBase, string expected)
    {
        var id = BingWallpaperService.ExtractId(urlBase);
        Assert.Equal(expected, id);
        Assert.All(id.ToCharArray(), c => Assert.False(Path.GetInvalidFileNameChars().Contains(c), $"非法文件名字符: {c}"));
    }

    [Fact]
    public void BuildThumbUrl_AppendsThumbResolution()
    {
        Assert.Equal("https://www.bing.com/th?id=OHR.Abc_400x240.jpg", BingWallpaperService.BuildThumbUrl("/th?id=OHR.Abc"));
    }

    [Theory]
    [InlineData("松岛海岸的象海豹（© Mint Images）", "松岛海岸的象海豹")]
    [InlineData("Frost flower (© photographer)", "Frost flower ")]
    [InlineData("无括号标题", "无括号标题")]
    public void ExtractTitle_SplitsAtFirstParen(string copyright, string expected)
    {
        var title = BingWallpaperService.ExtractTitle(copyright);
        Assert.Equal(expected.TrimEnd(), title.TrimEnd());
    }
}
