using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dnkhw.Core.Update;

/// <summary>官网 version.json 的解析模型（version 用字符串承载，比对统一走 VersionComparer）。</summary>
public sealed record UpdateInfo(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("notes")] string? Notes = null,
    [property: JsonPropertyName("downloadUrl")] string? DownloadUrl = null,
    [property: JsonPropertyName("sha256")] string? Sha256 = null,
    [property: JsonPropertyName("sizeBytes")] long? SizeBytes = null);

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    FetchFailed,
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Info = null, string? Error = null)
{
    public static UpdateCheckResult UpToDate() => new(UpdateCheckStatus.UpToDate);
    public static UpdateCheckResult Available(UpdateInfo info) => new(UpdateCheckStatus.UpdateAvailable, info);
    public static UpdateCheckResult Failed(string error) => new(UpdateCheckStatus.FetchFailed, null, error);
}

public static class VersionComparer
{
    /// <summary>
    /// 比较点分数字版本串（1-4 段，如 1.0.0 / 1.0.0.0，可带 v 前缀）。
    /// 非法输入按 0.0.0 处理；返回 -1/0/1。
    /// </summary>
    public static int Compare(string? left, string? right)
    {
        var a = Parse(left);
        var b = Parse(right);
        for (var i = 0; i < 4; i++)
        {
            if (a[i] != b[i])
            {
                return a[i] < b[i] ? -1 : 1;
            }
        }

        return 0;
    }

    private static int[] Parse(string? version)
    {
        var result = new int[4];
        if (string.IsNullOrWhiteSpace(version))
        {
            return result;
        }

        var trimmed = version.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        var parts = trimmed.Split('.');
        for (var i = 0; i < 4 && i < parts.Length; i++)
        {
            if (int.TryParse(parts[i], out var value) && value >= 0)
            {
                result[i] = value;
            }
        }

        return result;
    }
}

/// <summary>
/// 手动检查更新：读取官网 version.json 与当前版本比对。
/// 仅在用户点击时联网一次（应用不做任何后台静默检查）。
/// </summary>
public sealed class UpdateChecker
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly string _url;
    private readonly HttpClient _client;

    public UpdateChecker(string? url = null, HttpMessageHandler? handler = null)
    {
        _url = url ?? ProductLinks.VersionJson;
        _client = handler is null
            ? new HttpClient { Timeout = Timeout }
            : new HttpClient(handler, disposeHandler: false);
    }

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var response = await _client.GetAsync(_url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed($"官网返回 {(int)response.StatusCode}，暂时无法检查");
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseAndCompare(json, currentVersion);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient 超时也表现为取消，但用户并未取消
            return UpdateCheckResult.Failed("网络超时，请稍后再试");
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failed($"网络异常：{ex.Message}");
        }
    }

    /// <summary>解析 version.json 并与当前版本比对；internal 供单测直接验证。</summary>
    internal static UpdateCheckResult ParseAndCompare(string json, string currentVersion)
    {
        try
        {
            var info = JsonSerializer.Deserialize<UpdateInfo>(json);
            if (info is null || string.IsNullOrWhiteSpace(info.Version))
            {
                return UpdateCheckResult.Failed("版本信息为空");
            }

            return VersionComparer.Compare(info.Version, currentVersion) > 0
                ? UpdateCheckResult.Available(info)
                : UpdateCheckResult.UpToDate();
        }
        catch (JsonException)
        {
            return UpdateCheckResult.Failed("版本信息格式不正确");
        }
    }
}
