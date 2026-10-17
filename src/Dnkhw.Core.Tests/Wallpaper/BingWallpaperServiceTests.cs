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
    public void ExtractId_RemovesTraversalAndQueryCharacters()
    {
        var id = BingWallpaperService.ExtractId("/th?id=../../unsafe/name&rf=LaDigue");

        Assert.DoesNotContain('/', id);
        Assert.DoesNotContain('\\', id);
        Assert.DoesNotContain('&', id);
        Assert.NotEqual("..", id);
    }

    [Fact]
    public void WallpaperStore_RejectsPathsOutsideCacheDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dnkhw-wallpaper-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WallpaperStore(directory);

            Assert.Throws<ArgumentException>(() => store.GetFullPath("../outside.jpg"));
            Assert.Throws<ArgumentException>(() => store.GetFullPath("not-an-image.exe"));
            Assert.Equal(Path.Combine(directory, "safe.jpg"), store.GetFullPath("safe.jpg"), ignoreCase: true);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("https://www.bing.com/th?id=OHR.Abc", true)]
    [InlineData("https://tse4.mm.bing.net/th?id=OHR.Abc", true)]
    [InlineData("http://www.bing.com/th?id=OHR.Abc", false)]
    [InlineData("https://evilbing.com/image.jpg", false)]
    [InlineData("https://example.com/image.jpg", false)]
    public void WallpaperStore_OnlyAllowsBingHttpsHosts(string url, bool expected)
    {
        Assert.Equal(expected, WallpaperStore.IsAllowedBingUri(new Uri(url)));
    }

    [Fact]
    public void WallpaperStore_DefaultCandidates_EmptyPicturesFallsBackToLocalAppData()
    {
        var candidates = WallpaperStore.BuildDefaultDirectoryCandidates(
            picturesDirectory: string.Empty,
            localAppDataDirectory: @"C:\Users\tester\AppData\Local",
            tempDirectory: @"C:\Temp");

        Assert.Equal(
            [@"C:\Users\tester\AppData\Local\dnkhw\Wallpapers", @"C:\Temp\dnkhw\Wallpapers"],
            candidates);
        Assert.All(candidates, path => Assert.True(Path.IsPathFullyQualified(path)));
    }

    [Fact]
    public void WallpaperStore_DefaultCandidates_DeduplicatesRedirectedFolders()
    {
        var candidates = WallpaperStore.BuildDefaultDirectoryCandidates(
            picturesDirectory: @"D:\Pictures",
            localAppDataDirectory: @"D:\Pictures",
            tempDirectory: @"C:\Temp");

        Assert.Equal(2, candidates.Count);
        Assert.Equal(@"D:\Pictures\dnkhw\Wallpapers", candidates[0]);
    }

    [Fact]
    public void WallpaperService_RejectsPngBeforeCallingWindowsApi()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dnkhw-wallpaper-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);

            var exception = Assert.Throws<InvalidDataException>(() =>
                WallpaperService.Set(path, WallpaperFit.Fill));

            Assert.Contains("BMP/JPG", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildThumbUrl_AppendsThumbResolution()
    {
        Assert.Equal("https://www.bing.com/th?id=OHR.Abc_400x240.jpg", BingWallpaperService.BuildThumbUrl("/th?id=OHR.Abc"));
    }

    [Fact]
    public void BuildRequestUrl_ClampsIndexAndCount()
    {
        var url = BingWallpaperService.BuildRequestUrl(index: -5, count: 99, market: "zh-CN");

        Assert.Contains("idx=0", url);
        Assert.Contains("n=8", url);
        Assert.Contains("mkt=zh-CN", url);
    }

    [Fact]
    public void BuildRequestUrl_SecondPageUsesOffsetEight()
    {
        var url = BingWallpaperService.BuildRequestUrl(index: 8, count: 8, market: "en-US");

        Assert.Equal("https://www.bing.com/HPImageArchive.aspx?format=js&idx=8&n=8&mkt=en-US", url);
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    [InlineData("ja-JP")]
    [InlineData("en-GB")]
    [InlineData("de-DE")]
    public void BuildRequestUrl_AcceptsSupportedMarkets(string market)
    {
        Assert.Contains($"mkt={market}", BingWallpaperService.BuildRequestUrl(0, 8, market));
    }

    [Theory]
    [InlineData("")]
    [InlineData("xx-XX")]
    [InlineData("zh-CN&hack=1")]
    [InlineData("../../inject")]
    public void BuildRequestUrl_RejectsUnknownMarket(string market)
    {
        Assert.Throws<ArgumentException>(() => BingWallpaperService.BuildRequestUrl(0, 8, market));
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
