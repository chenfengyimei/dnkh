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
    [Fact]
    public void ParseAndCompare_NewerRemote_ReturnsAvailable()
    {
        var json = """
            {
              "version": "1.1.0",
              "notes": "新增设置页",
              "downloadUrl": "https://www.cyhaoka.vip/#download",
              "sha256": "ABC",
              "sizeBytes": 70000000
            }
            """;

        var result = UpdateChecker.ParseAndCompare(json, "1.0.0");

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.1.0", result.Info!.Version);
        Assert.Equal("新增设置页", result.Info.Notes);
        Assert.Equal("ABC", result.Info.Sha256);
        Assert.Equal(70000000, result.Info.SizeBytes);
    }

    [Fact]
    public void ParseAndCompare_SameVersion_ReturnsUpToDate()
    {
        var json = """{ "version": "1.0.0" }""";

        var result = UpdateChecker.ParseAndCompare(json, "1.0.0");

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public void ParseAndCompare_OlderRemote_ReturnsUpToDate()
    {
        var json = """{ "version": "0.9.5" }""";

        var result = UpdateChecker.ParseAndCompare(json, "1.0.0");

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public void ParseAndCompare_MalformedJson_ReturnsFetchFailed()
    {
        var result = UpdateChecker.ParseAndCompare("not json", "1.0.0");

        Assert.Equal(UpdateCheckStatus.FetchFailed, result.Status);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ParseAndCompare_MissingVersion_ReturnsFetchFailed()
    {
        var result = UpdateChecker.ParseAndCompare("""{ "notes": "x" }""", "1.0.0");

        Assert.Equal(UpdateCheckStatus.FetchFailed, result.Status);
    }
}

/// <summary>离线 HttpClient：不开真实网络即可覆盖 HTTP 各分支。</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(respond(request));
}

public class UpdateCheckerHttpTests
{
    private const string JsonUrl = "https://example.test/version.json";

    [Fact]
    public async Task CheckAsync_HttpError_ReturnsFetchFailed()
    {
        var checker = new UpdateChecker(JsonUrl, new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await checker.CheckAsync("1.0.0", TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.FetchFailed, result.Status);
        Assert.Contains("404", result.Error);
    }

    [Fact]
    public async Task CheckAsync_SuccessNewer_ReturnsAvailable()
    {
        var json = """{ "version": "1.0.1", "downloadUrl": "https://www.cyhaoka.vip/#download" }""";
        var checker = new UpdateChecker(JsonUrl, new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        }));

        var result = await checker.CheckAsync("1.0.0", TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.0.1", result.Info!.Version);
    }

    [Fact]
    public async Task CheckAsync_NetworkThrow_ReturnsFetchFailed()
    {
        var checker = new UpdateChecker(JsonUrl, new FakeHandler(_ => throw new HttpRequestException("无法连接")));

        var result = await checker.CheckAsync("1.0.0", TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.FetchFailed, result.Status);
        Assert.Contains("无法连接", result.Error);
    }
}
