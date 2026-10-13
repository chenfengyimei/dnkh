using System.Net.Http;
using System.Security.Cryptography;

namespace Dnkhw.Core.Update;

public sealed record UpdateDownloadProgress(long BytesReceived, long TotalBytes, double Percent)
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

        Directory.CreateDirectory(DownloadDirectory);
        var tempPath = Path.Combine(DownloadDirectory, "Dnkhw.download.tmp");
        await DownloadToFileAsync(info.DirectUrl, tempPath, progress, ct).ConfigureAwait(false);

        var actual = await HashFileAsync(tempPath, ct).ConfigureAwait(false);
        if (!string.Equals(actual, info.Sha256.Trim().ToUpperInvariant(), StringComparison.Ordinal))
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // 清理失败不影响报错
            }

            throw new InvalidOperationException("下载文件校验不通过（可能传输损坏），已放弃并清理");
        }

        File.Move(tempPath, ReadyPath, overwrite: true);
        return ReadyPath;
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
}
