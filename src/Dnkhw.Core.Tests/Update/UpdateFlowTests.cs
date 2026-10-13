using System.Net;
using System.Security.Cryptography;
using Dnkhw.Core.Update;

namespace Dnkhw.Core.Tests.Update;

/// <summary>把固定字节流当作下载响应。</summary>
internal sealed class BytesHandler(byte[] payload, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(payload),
        });
}

public class UpdateDownloaderTests
{
    private static UpdateInfo MakeInfo(byte[] payload, string url = "https://dl.test/Dnkhw.exe")
        => new(
            Version: "1.0.1",
            Notes: "test",
            DownloadUrl: url,
            DirectUrl: url,
            Sha256: Convert.ToHexString(SHA256.HashData(payload)),
            SizeBytes: payload.Length,
            Source: UpdateSourceKind.VersionJson);

    [Fact]
    public async Task Download_CorrectHash_ReturnsReadyPathWithContent()
    {
        var payload = "new binary content"u8.ToArray();
        using var handler = new BytesHandler(payload);
        var downloader = new UpdateDownloader(handler);
        try
        {
            var info = MakeInfo(payload);
            var result = await downloader.DownloadAsync(info, null, TestContext.Current.CancellationToken);

            Assert.Equal(UpdateDownloader.ReadyPath, result);
            Assert.Equal(payload, await File.ReadAllBytesAsync(result, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (File.Exists(UpdateDownloader.ReadyPath))
            {
                File.Delete(UpdateDownloader.ReadyPath);
            }
        }
    }

    [Fact]
    public async Task Download_WrongHash_ThrowsAndCleansTemp()
    {
        var payload = "new binary content"u8.ToArray();
        using var handler = new BytesHandler(payload);
        var downloader = new UpdateDownloader(handler);
        var info = MakeInfo(payload) with { Sha256 = new string('D', 64) };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => downloader.DownloadAsync(info, null, TestContext.Current.CancellationToken));

        Assert.Empty(Directory.EnumerateFiles(UpdateDownloader.DownloadDirectory, "Dnkhw.*.download.tmp"));
    }

    [Fact]
    public async Task Download_MissingHash_Refuses()
    {
        using var handler = new BytesHandler("x"u8.ToArray());
        var downloader = new UpdateDownloader(handler);
        var info = new UpdateInfo("1.0.1", DirectUrl: "https://dl.test/Dnkhw.exe", Sha256: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => downloader.DownloadAsync(info, null, TestContext.Current.CancellationToken));
        Assert.Contains("哈希", ex.Message);
    }

    [Fact]
    public async Task Download_GiteeSource_Refuses()
    {
        using var handler = new BytesHandler("x"u8.ToArray());
        var downloader = new UpdateDownloader(handler);
        var info = new UpdateInfo("1.0.1", DirectUrl: "https://dl.test/Dnkhw.exe", Sha256: "AA")
        {
            Source = UpdateSourceKind.GiteeRelease,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => downloader.DownloadAsync(info, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Download_HttpError_Throws()
    {
        using var handler = new BytesHandler("x"u8.ToArray(), HttpStatusCode.Forbidden);
        var downloader = new UpdateDownloader(handler);
        var info = new UpdateInfo(
            "1.0.1",
            DirectUrl: "https://dl.test/Dnkhw.exe",
            Sha256: new string('A', 64),
            SizeBytes: 1);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => downloader.DownloadAsync(info, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Download_ReportsProgressTo100()
    {
        var payload = new byte[300_000];
        Random.Shared.NextBytes(payload);
        using var handler = new BytesHandler(payload);
        var downloader = new UpdateDownloader(handler);
        var info = MakeInfo(payload);
        var reports = new List<UpdateDownloadProgress>();
        var progress = new SyncProgress(reports.Add);

        try
        {
            await downloader.DownloadAsync(info, progress, TestContext.Current.CancellationToken);
        }
        finally
        {
            if (File.Exists(UpdateDownloader.ReadyPath))
            {
                File.Delete(UpdateDownloader.ReadyPath);
            }
        }

        Assert.NotEmpty(reports);
        Assert.Equal(100, reports[^1].Percent);
        Assert.Equal(payload.LongLength, reports[^1].BytesReceived);
        Assert.True(reports[^1].IsVerifying);
    }

    [Fact]
    public async Task Download_WrongSize_ThrowsAndCleansTemp()
    {
        var payload = "new binary content"u8.ToArray();
        using var handler = new BytesHandler(payload);
        var downloader = new UpdateDownloader(handler);
        var info = MakeInfo(payload) with { SizeBytes = payload.Length + 1 };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => downloader.DownloadAsync(info, null, TestContext.Current.CancellationToken));

        Assert.Contains("大小", ex.Message);
        Assert.Empty(Directory.EnumerateFiles(UpdateDownloader.DownloadDirectory, "Dnkhw.*.download.tmp"));
    }

    [Theory]
    [InlineData("http://dl.test/Dnkhw.exe", "HTTPS")]
    [InlineData("https://dl.test/Dnkhw.exe", "SHA256")]
    public async Task Download_InvalidMetadata_RefusesBeforeDownload(string url, string expectedMessage)
    {
        using var handler = new BytesHandler("x"u8.ToArray());
        var downloader = new UpdateDownloader(handler);
        var info = new UpdateInfo(
            "1.0.1",
            DirectUrl: url,
            Sha256: url.StartsWith("http:", StringComparison.Ordinal) ? new string('A', 64) : "invalid",
            SizeBytes: 1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => downloader.DownloadAsync(info, null, TestContext.Current.CancellationToken));

        Assert.Contains(expectedMessage, ex.Message);
    }

    /// <summary>同步记录（xUnit 无同步上下文，Progress<T> 会异步投递产生竞态）。</summary>
    private sealed class SyncProgress(Action<UpdateDownloadProgress> onReport) : IProgress<UpdateDownloadProgress>
    {
        public void Report(UpdateDownloadProgress value) => onReport(value);
    }
}

public class SelfUpdaterTests
{
    private static string TempDir() => Path.Combine(
        Path.GetTempPath(),
        $"dnkhw-selfup-{Guid.NewGuid():N}");

    [Fact]
    public void Apply_SwapsFilesAndCleansBackup()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var exePath = Path.Combine(dir, "app.exe");
            var ready = Path.Combine(dir, "ready.exe");
            File.WriteAllText(exePath, "OLD");
            File.WriteAllText(ready, "NEW");

            SelfUpdater.Apply(ready, exePath);

            Assert.Equal("NEW", File.ReadAllText(exePath));
            Assert.False(File.Exists(ready));
            Assert.False(File.Exists(exePath + ".old"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Apply_MissingReadyFile_Throws()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var exePath = Path.Combine(dir, "app.exe");
            File.WriteAllText(exePath, "OLD");

            Assert.Throws<FileNotFoundException>(() => SelfUpdater.Apply(Path.Combine(dir, "missing.exe"), exePath));
            Assert.Equal("OLD", File.ReadAllText(exePath));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Apply_NewFileMoveFails_RestoresOld()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var exePath = Path.Combine(dir, "app.exe");
            var ready = Path.Combine(dir, "ready.exe");
            File.WriteAllText(exePath, "OLD");
            File.WriteAllText(ready, "NEW");

            // 独占句柄锁住 ready：第二步 Move（ready→exe）抛 IOException，触发回滚
            using var lockStream = new FileStream(ready, FileMode.Open, FileAccess.Read, FileShare.None);

            Assert.Throws<IOException>(() => SelfUpdater.Apply(ready, exePath));
            Assert.Equal("OLD", File.ReadAllText(exePath));
            Assert.False(File.Exists(exePath + ".old"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Cleanup_RemovesOldBinaryAndStaleDownload()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var exePath = Path.Combine(dir, "app.exe");
            var old = exePath + ".old";
            var ready = Path.Combine(dir, "Dnkhw.exe");
            File.WriteAllText(exePath, "CUR");
            File.WriteAllText(old, "OLD");

            SelfUpdater.Cleanup(exePath, ready);
            Assert.False(File.Exists(old));

            // 过期（>7 天）下载残留被清；新鲜保留
            Directory.CreateDirectory(Path.GetDirectoryName(ready)!);
            File.WriteAllText(ready, "STALE");
            File.SetLastWriteTimeUtc(ready, DateTime.UtcNow.AddDays(-8));
            SelfUpdater.Cleanup(exePath, ready);
            Assert.False(File.Exists(ready));

            File.WriteAllText(ready, "FRESH");
            SelfUpdater.Cleanup(exePath, ready);
            Assert.True(File.Exists(ready));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BuildHelperScript_UsesEncodedPathsAndWaitsForCurrentProcess()
    {
        var script = SelfUpdater.BuildHelperScript(
            processId: 1234,
            exePath: @"C:\Program Files\dnkhw\Dnkhw.exe",
            readyPath: @"C:\Users\test\AppData\Local\dnkhw\update\Dnkhw.exe",
            restartAfterApply: true);

        Assert.Contains("Get-Process -Id $processId", script);
        Assert.Contains("Remove-Item -LiteralPath $backup", script);
        Assert.Contains("Move-Item -LiteralPath $target", script);
        Assert.Contains("Start-Process -FilePath $target", script);
        Assert.DoesNotContain(@"C:\Program Files\dnkhw\Dnkhw.exe", script);
    }
}
