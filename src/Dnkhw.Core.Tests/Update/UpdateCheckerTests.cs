using System.Net;
using Dnkhw.Core.Update;

namespace Dnkhw.Core.Tests.Update;

public class VersionComparerTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.0.0.0", "1.0.0", 0)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1.9.9", "1.10.0", -1)]
    [InlineData("v1.2.0", "1.2.1", -1)]
    [InlineData("V2.0.0", "1.9.9", 1)]
    [InlineData("", "0.0.1", -1)]
    [InlineData(null, null, 0)]
    [InlineData("garbage", "0.0.1", -1)]
    public void Compare_HandlesVersionShapes(string? left, string? right, int expected)
    {
        Assert.Equal(expected, Math.Sign(VersionComparer.Compare(left, right)));
    }
}

public class UpdateCheckerParsingTests
{
    private const string VersionJson = """
        {
          "version": "1.1.0",
          "notes": "新增在线更新",
          "downloadUrl": "https://app.cyhaoka.vip/#download",
          "directUrl": "https://app.cyhaoka.vip/download/Dnkhw.exe",
          "sha256": "abc123",
          "sizeBytes": 70000000
        }
        """;

    private const string GitHubJson = """
        {
          "tag_name": "v1.1.0",
          "body": "发布说明第一行\n第二行",
          "html_url": "https://github.com/owner/dnkhw/releases/tag/v1.1.0",
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://example/notes.txt", "size": 10 },
            { "name": "Dnkhw.exe", "browser_download_url": "https://example/Dnkhw.exe",
              "size": 70000000, "digest": "sha256:FEEDBEEF" }
          ]
        }
        """;

    private const string GiteeJson = """
        {
          "tag_name": "v1.1.0",
          "body": "发布说明",
          "html_url": "https://gitee.com/owner/dnkhw/releases/v1.1.0",
          "assets": [
            { "name": "Dnkhw.exe", "browser_download_url": "https://gitee.com/attachments/Dnkhw.exe" }
          ]
        }
        """;

    [Fact]
    public void Parse_VersionJson_IncludesDirectUrlAndHash()
    {
        var result = UpdateChecker.ParseAndCompare(VersionJson, "1.0.0", UpdateSourceKind.VersionJson);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        var info = result.Info!;
        Assert.Equal("1.1.0", info.Version);
        Assert.Equal("https://app.cyhaoka.vip/download/Dnkhw.exe", info.DirectUrl);
        Assert.Equal("abc123", info.Sha256);
        Assert.Equal(70000000, info.SizeBytes);
        Assert.Equal(UpdateSourceKind.VersionJson, info.Source);
    }

    [Fact]
    public void Parse_GitHubRelease_ExtractsAssetUrlAndDigest()
    {
        var result = UpdateChecker.ParseAndCompare(GitHubJson, "1.0.0", UpdateSourceKind.GitHubRelease);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        var info = result.Info!;
        Assert.Equal("1.1.0", info.Version);
        Assert.Equal("https://example/Dnkhw.exe", info.DirectUrl);
        Assert.Equal("FEEDBEEF", info.Sha256);
        Assert.Equal(70000000, info.SizeBytes);
        Assert.Equal(UpdateSourceKind.GitHubRelease, info.Source);
    }

    [Fact]
    public void Parse_GitHubRelease_WithoutMatchingAsset_HasNoDirectUrl()
    {
        var json = """{ "tag_name": "v1.1.0", "assets": [ { "name": "other.bin", "browser_download_url": "https://x/y" } ] }""";

        var result = UpdateChecker.ParseAndCompare(json, "1.0.0", UpdateSourceKind.GitHubRelease);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Null(result.Info!.DirectUrl);
    }

    [Fact]
    public void Parse_GiteeRelease_HasUrlButNoHash()
    {
        var result = UpdateChecker.ParseAndCompare(GiteeJson, "1.0.0", UpdateSourceKind.GiteeRelease);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        var info = result.Info!;
        Assert.Equal("https://gitee.com/attachments/Dnkhw.exe", info.DirectUrl);
        Assert.Null(info.Sha256);
        Assert.Equal(UpdateSourceKind.GiteeRelease, info.Source);
    }

    [Fact]
    public void Parse_SameVersion_ReturnsUpToDate()
    {
        Assert.Equal(
            UpdateCheckStatus.UpToDate,
            UpdateChecker.ParseAndCompare(GitHubJson, "1.1.0", UpdateSourceKind.GitHubRelease).Status);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsFetchFailed()
    {
        Assert.Equal(
            UpdateCheckStatus.FetchFailed,
            UpdateChecker.ParseAndCompare("not json", "1.0.0", UpdateSourceKind.VersionJson).Status);
    }

    [Fact]
    public void Parse_MissingVersion_ReturnsFetchFailed()
    {
        var result = UpdateChecker.ParseAndCompare("""{ "notes": "x" }""", "1.0.0", UpdateSourceKind.VersionJson);

        Assert.Equal(UpdateCheckStatus.FetchFailed, result.Status);
    }

    [Theory]
    [InlineData("sha256:abc", "ABC")]
    [InlineData("sha256:ABC", "ABC")]
    [InlineData("ABC", "ABC")]
    public void ExtractSha256_Variants(string digest, string expected)
    {
        Assert.Equal(expected, UpdateChecker.ExtractSha256(digest));
    }

    [Fact]
    public void ExtractSha256_NullStaysNull()
    {
        Assert.Null(UpdateChecker.ExtractSha256(null));
    }
}

/// <summary>离线 HttpClient：按请求 URL 路由响应，可发流式字节。</summary>
internal sealed class RoutingHandler(Dictionary<string, Func<HttpResponseMessage>> routes) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(routes.TryGetValue(
            request.RequestUri?.ToString() ?? string.Empty,
            out var respond)
            ? respond()
            : new HttpResponseMessage(HttpStatusCode.NotFound));
}

public class UpdateCheckerMultiSourceTests
{
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body),
    };

    [Fact]
    public async Task Check_FallsThroughFailingSourcesToOfficial()
    {
        var checker = new UpdateChecker(
            new RoutingHandler(new Dictionary<string, Func<HttpResponseMessage>>
            {
                ["https://git.test/api"] = () => new HttpResponseMessage(HttpStatusCode.NotFound),
                ["https://gitee.test/api"] = () => new HttpResponseMessage(HttpStatusCode.Forbidden),
                ["https://official.test/version.json"] = () => Json(
                    """{ "version": "1.1.0", "directUrl": "https://official.test/dl.exe", "sha256": "AA" }"""),
            }),
            [
                new UpdateSource(UpdateSourceKind.GitHubRelease, "https://git.test/api"),
                new UpdateSource(UpdateSourceKind.GiteeRelease, "https://gitee.test/api"),
                new UpdateSource(UpdateSourceKind.VersionJson, "https://official.test/version.json"),
            ]);

        var result = await checker.CheckAsync("1.0.0", TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(UpdateSourceKind.VersionJson, result.Info!.Source);
    }

    [Fact]
    public async Task Check_FirstSourceWins_WhenItGivesVerdict()
    {
        var checker = new UpdateChecker(
            new RoutingHandler(new Dictionary<string, Func<HttpResponseMessage>>
            {
                ["https://git.test/api"] = () => Json(
                    """{ "tag_name": "v1.0.1", "assets": [ { "name": "Dnkhw.exe", "browser_download_url": "https://git.test/dl", "digest": "sha256:BB" } ] }"""),
                ["https://official.test/version.json"] = () => Json("""{ "version": "9.9.9" }"""),
            }),
            [
                new UpdateSource(UpdateSourceKind.GitHubRelease, "https://git.test/api"),
                new UpdateSource(UpdateSourceKind.VersionJson, "https://official.test/version.json"),
            ]);

        var result = await checker.CheckAsync("1.0.0", TestContext.Current.CancellationToken);

        // 排在前面的 git 源给出明确结论后立即返回，不再看后面的源
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.0.1", result.Info!.Version);
        Assert.Equal(UpdateSourceKind.GitHubRelease, result.Info.Source);
    }

    [Fact]
    public async Task Check_UpToDateFromFirstSource_ReturnsImmediately()
    {
        var checker = new UpdateChecker(
            new RoutingHandler(new Dictionary<string, Func<HttpResponseMessage>>
            {
                ["https://git.test/api"] = () => Json(
                    """{ "tag_name": "v1.0.0", "assets": [ { "name": "Dnkhw.exe", "browser_download_url": "https://git.test/dl", "digest": "sha256:BB" } ] }"""),
            }),
            [new UpdateSource(UpdateSourceKind.GitHubRelease, "https://git.test/api")]);

        var result = await checker.CheckAsync("1.0.0", TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task Check_AllSourcesFail_ReportsLastError()
    {
        var checker = new UpdateChecker(
            new RoutingHandler([]),
            [
                new UpdateSource(UpdateSourceKind.GitHubRelease, "https://git.test/api"),
                new UpdateSource(UpdateSourceKind.VersionJson, "https://official.test/version.json"),
            ]);

        var result = await checker.CheckAsync("1.0.0", TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.FetchFailed, result.Status);
        Assert.Contains("官网", result.Error);
        Assert.Contains("404", result.Error);
    }
}
