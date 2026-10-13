using System.Net.Http;
using System.Security.Cryptography;

namespace Dnkhw.Core.Update;

public sealed record UpdateDownloadProgress(
    long BytesReceived,
    long TotalBytes,
    double Percent,
    bool IsVerifying = false)
{
    public string SizeText => Dnkhw.Core.Formatting.SizeFormatter.Format(BytesReceived)
        + (TotalBytes > 0 ? $" / {Dnkhw.Core.Formatting.SizeFormatter.Format(TotalBytes)}" : string.Empty);
}

/// <summary>
/// 更新包下载：流式写入临时文件 + SHA256 强校验。
/// 安全底线：更新源未提供哈希或校验不过，一律拒绝返回文件——绝不替换未校验的 exe。
/// </summary>
public sealed class UpdateDownloader
{
    private const long MaxPackageBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan OverallDownloadTimeout = TimeSpan.FromMinutes(20);

    /// <summary>下载目录（固定在用户本地数据下，避免权限与 exe 目录只读问题）。</summary>
    public static string DownloadDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "dnkhw",
        "update");

    /// <summary>下载完成的就绪文件路径（SelfUpdater 从这里取）。</summary>
    public static string ReadyPath => Path.Combine(DownloadDirectory, UpdateSources.AssetFileName);

    private readonly HttpClient _client;

    public UpdateDownloader(HttpMessageHandler? handler = null)
    {
        _client = handler is null
            ? new HttpClient { Timeout = TimeSpan.FromMinutes(15) }
            : new HttpClient(handler, disposeHandler: false);
    }

    /// <summary>下载并校验；成功返回就绪文件路径。取消/校验失败抛异常。</summary>
    public async Task<string> DownloadAsync(
        UpdateInfo info,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (info.Source is UpdateSourceKind.GiteeRelease || string.IsNullOrWhiteSpace(info.Sha256))
        {
            throw new InvalidOperationException("该更新源未提供校验哈希，不能在线更新，请前往官网下载");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(info.DirectUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(info.Sha256);
        ValidateInfo(info);

        Directory.CreateDirectory(DownloadDirectory);
        // 每次下载使用独立临时文件：重复点击/恢复操作不会互相踩文件。
        var tempPath = Path.Combine(DownloadDirectory, $"Dnkhw.{Guid.NewGuid():N}.download.tmp");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(OverallDownloadTimeout);
        try
        {
            await DownloadToFileAsync(info.DirectUrl, tempPath, progress, timeoutCts.Token).ConfigureAwait(false);

            var downloadedSize = new FileInfo(tempPath).Length;
            if (downloadedSize != info.SizeBytes)
            {
                throw new InvalidOperationException("下载文件大小不正确，已放弃并清理");
            }

            // 网络传输结束不等于更新已就绪：大文件哈希校验仍可能需要数秒。
            progress?.Report(new UpdateDownloadProgress(
                downloadedSize,
                info.SizeBytes.Value,
                100,
                IsVerifying: true));
            var actual = await HashFileAsync(tempPath, timeoutCts.Token).ConfigureAwait(false);
            if (!string.Equals(actual, info.Sha256.Trim().ToUpperInvariant(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("下载文件校验不通过（可能传输损坏），已放弃并清理");
            }

            File.Move(tempPath, ReadyPath, overwrite: true);
            return ReadyPath;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"下载或校验更新包超过 {OverallDownloadTimeout.TotalMinutes:0} 分钟，已停止");
        }
        finally
        {
            // 成功时文件已经移动；取消、超时、断网与校验失败都不留下半包。
            TryDelete(tempPath);
        }
    }

    private static void ValidateInfo(UpdateInfo info)
    {
        if (!Uri.TryCreate(info.DirectUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("更新下载地址必须使用 HTTPS");
        }

        var hash = info.Sha256!.Trim();
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("更新包 SHA256 格式无效，已拒绝下载");
        }

        if (info.SizeBytes is not > 0 or > MaxPackageBytes)
        {
            throw new InvalidOperationException("更新包大小信息无效或超过安全上限，已拒绝下载");
        }
    }

    private async Task DownloadToFileAsync(
        string url,
        string path,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken ct)
    {
        // ResponseHeadersRead：流式落盘，不整包进内存（67MB 级文件）
        using var response = await _client.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"下载失败：服务器返回 {(int)response.StatusCode}");
        }

        var total = response.Content.Headers.ContentLength ?? 0;
        if (total > MaxPackageBytes)
        {
            throw new InvalidDataException("更新包超过 512 MB 安全上限");
        }
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);

        var buffer = new byte[64 * 1024];
        long received = 0;
        int read;
        var lastReport = DateTime.MinValue;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;
            if (received > MaxPackageBytes)
            {
                throw new InvalidDataException("更新包超过 512 MB 安全上限");
            }

            // 进度节流：每 ~200ms 上报一次，避免 UI 刷新洪峰
            var now = DateTime.UtcNow;
            if (progress is not null && (now - lastReport).TotalMilliseconds > 200)
            {
                lastReport = now;
                progress.Report(new UpdateDownloadProgress(
                    received, total, total > 0 ? received * 100.0 / total : 0));
            }
        }

        if (progress is not null)
        {
            progress.Report(new UpdateDownloadProgress(received, total, 100));
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // 清理失败不影响向用户报告下载错误
        }
    }
}
