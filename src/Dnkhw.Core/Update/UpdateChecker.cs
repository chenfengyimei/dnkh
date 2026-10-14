using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Dnkhw.Core.Update;

/// <summary>一次可用更新的完整信息（多个来源归一后的统一模型）。</summary>
public sealed record UpdateInfo(
    string Version,
    string? Notes = null,
    string? DownloadUrl = null,
    string? DirectUrl = null,
    string? Sha256 = null,
    long? SizeBytes = null,
    UpdateSourceKind Source = UpdateSourceKind.VersionJson);

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

    /// <summary>更新源版本必须是 1-4 段非负数字（可带 v 前缀）；避免损坏清单被误判为“已是最新”。</summary>
    internal static bool IsValidVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var trimmed = version.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        var parts = trimmed.Split('.');
        return parts.Length is >= 1 and <= 4
            && parts.All(part => part.Length > 0 && uint.TryParse(part, out _));
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
/// 手动检查更新：并发探测 Gitee、GitHub 与官网，从全部可用源中选择最高版本；
/// 同版本优先选择带 SHA256 的可自动更新来源，再选择响应最快的来源。
/// 仅在用户点击时联网（应用不做任何后台静默检查）。
/// </summary>
public sealed class UpdateChecker
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Regex GiteeExeSha256Pattern = new(
        @"(?im)\bDnkhw\.exe\b[^\r\n]*?\bSHA-?256\s*[:=]\s*(?<hash>[a-f0-9]{64})\b",
        RegexOptions.CultureInvariant);

    private readonly IReadOnlyList<UpdateSource> _sources;
    private readonly HttpClient _client;

    public UpdateChecker(HttpMessageHandler? handler = null, IReadOnlyList<UpdateSource>? sources = null)
    {
        _sources = sources ?? UpdateSources.Defaults;
        _client = handler is null
            ? new HttpClient { Timeout = Timeout }
            : new HttpClient(handler, disposeHandler: false);
    }

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        var outcomes = await Task.WhenAll(_sources.Select(source => CheckSourceAsync(source, currentVersion, ct))).ConfigureAwait(false);
        SourceCheckOutcome? bestUpdate = null;
        var hasUsableSource = false;
        foreach (var outcome in outcomes)
        {
            var result = outcome.Result;
            if (result.Status == UpdateCheckStatus.FetchFailed)
            {
                continue;
            }

            hasUsableSource = true;
            if (result.Status == UpdateCheckStatus.UpdateAvailable
                && result.Info is not null
                && IsBetterCandidate(outcome, bestUpdate))
            {
                bestUpdate = outcome;
            }
        }

        if (bestUpdate is not null)
        {
            return bestUpdate.Result;
        }

        if (hasUsableSource)
        {
            return UpdateCheckResult.UpToDate();
        }

        var errors = outcomes
            .Where(outcome => outcome.Result.Status == UpdateCheckStatus.FetchFailed)
            .Select(outcome => outcome.Result.Error)
            .Where(error => !string.IsNullOrWhiteSpace(error));
        return UpdateCheckResult.Failed($"检查失败：{string.Join("；", errors.DefaultIfEmpty("没有可用的更新源"))}");
    }

    private async Task<SourceCheckOutcome> CheckSourceAsync(UpdateSource source, string currentVersion, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
            if (source.Kind == UpdateSourceKind.GitHubRelease)
            {
                // GitHub API 强制要求 User-Agent，缺省直接 403
                request.Headers.UserAgent.ParseAdd("dnkhw-updater/1.0 (+https://www.cyhaoka.vip)");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
            }

            using var response = await _client.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new SourceCheckOutcome(source,
                    UpdateCheckResult.Failed($"{SourceName(source.Kind)}返回 {(int)response.StatusCode}"), stopwatch.Elapsed);
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var result = ParseAndCompare(json, currentVersion, source.Kind);
            if (result.Status == UpdateCheckStatus.FetchFailed)
            {
                result = UpdateCheckResult.Failed($"{SourceName(source.Kind)}：{result.Error}");
            }

            return new SourceCheckOutcome(source, result, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new SourceCheckOutcome(source, UpdateCheckResult.Failed($"{SourceName(source.Kind)}超时"), stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SourceCheckOutcome(source,
                UpdateCheckResult.Failed($"{SourceName(source.Kind)}网络异常：{ex.Message}"), stopwatch.Elapsed);
        }
    }

    private static bool IsBetterCandidate(SourceCheckOutcome candidate, SourceCheckOutcome? current)
    {
        if (current?.Result.Info is null)
        {
            return true;
        }

        var candidateInfo = candidate.Result.Info!;
        var currentInfo = current.Result.Info;

        var versionComparison = VersionComparer.Compare(candidateInfo.Version, currentInfo.Version);
        if (versionComparison != 0)
        {
            return versionComparison > 0;
        }

        var candidateVerified = CanAutoUpdate(candidateInfo);
        var currentVerified = CanAutoUpdate(currentInfo);
        if (candidateVerified != currentVerified)
        {
            return candidateVerified;
        }

        // 在相同版本、相同校验能力时，直接选择更快连通的来源。
        return candidate.Elapsed < current.Elapsed;
    }

    private static bool CanAutoUpdate(UpdateInfo info) =>
        !string.IsNullOrWhiteSpace(info.DirectUrl)
        && !string.IsNullOrWhiteSpace(info.Sha256)
        && info.SizeBytes is > 0;

    private sealed record SourceCheckOutcome(UpdateSource Source, UpdateCheckResult Result, TimeSpan Elapsed);

    public static string SourceName(UpdateSourceKind kind) => kind switch
    {
        UpdateSourceKind.GitHubRelease => "GitHub",
        UpdateSourceKind.GiteeRelease => "Gitee",
        _ => "官网",
    };

    /// <summary>按源类型解析并比对；internal 供单测直接验证。</summary>
    internal static UpdateCheckResult ParseAndCompare(string json, string currentVersion, UpdateSourceKind kind)
    {
        try
        {
            var info = kind switch
            {
                UpdateSourceKind.GitHubRelease => ParseGitHubRelease(json),
                UpdateSourceKind.GiteeRelease => ParseGiteeRelease(json),
                _ => ParseVersionJson(json),
            };

            if (info is null || !VersionComparer.IsValidVersion(info.Version))
            {
                return UpdateCheckResult.Failed("版本信息为空或格式不正确");
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

    /// <summary>官网 version.json：directUrl 为 exe 直链（在线更新用）。</summary>
    private static UpdateInfo? ParseVersionJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new UpdateInfo(
            Version: root.GetString("version") ?? string.Empty,
            Notes: root.GetString("notes"),
            DownloadUrl: root.GetString("downloadUrl"),
            DirectUrl: root.GetString("directUrl"),
            Sha256: root.GetString("sha256"),
            SizeBytes: root.TryGetInt64("sizeBytes", out var size) ? size : null,
            Source: UpdateSourceKind.VersionJson);
    }

    /// <summary>GitHub/Gitee 的 tag 通常带 v 前缀，统一剥掉对齐 version.json 的纯数字约定。</summary>
    internal static string? StripTagPrefix(string? tag) =>
        string.IsNullOrWhiteSpace(tag) ? tag : tag.Trim().TrimStart('v', 'V');

    /// <summary>GitHub latest release：tag_name + assets[].browser_download_url + digest(sha256:...)。</summary>
    private static UpdateInfo? ParseGitHubRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var info = new UpdateInfo(
            Version: StripTagPrefix(root.GetString("tag_name")) ?? string.Empty,
            Notes: root.GetString("body"),
            DownloadUrl: root.GetString("html_url"),
            DirectUrl: null,
            Sha256: null,
            SizeBytes: null,
            Source: UpdateSourceKind.GitHubRelease);
        if (!root.TryGetProperty("assets", out var assets))
        {
            return info;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            if (!string.Equals(asset.GetString("name"), UpdateSources.AssetFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return info with
            {
                DirectUrl = asset.GetString("browser_download_url"),
                Sha256 = ExtractSha256(asset.GetString("digest")),
                SizeBytes = asset.TryGetInt64("size", out var size) ? size : null,
            };
        }

        return info;
    }

    /// <summary>
    /// Gitee latest release：API 不提供附件 digest；发布说明中若带
    /// “Dnkhw.exe SHA256: &lt;64位哈希&gt;”，即可安全参与在线自更新，否则仅引导下载页。
    /// </summary>
    private static UpdateInfo? ParseGiteeRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var info = new UpdateInfo(
            Version: StripTagPrefix(root.GetString("tag_name")) ?? string.Empty,
            Notes: root.GetString("body"),
            DownloadUrl: root.GetString("html_url"),
            DirectUrl: null,
            Sha256: null,
            SizeBytes: null,
            Source: UpdateSourceKind.GiteeRelease);
        if (!root.TryGetProperty("assets", out var assets))
        {
            return info;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            if (!string.Equals(asset.GetString("name"), UpdateSources.AssetFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return info with
            {
                DirectUrl = asset.GetString("browser_download_url"),
                Sha256 = ExtractGiteeExeSha256(info.Notes),
                SizeBytes = asset.TryGetInt64("size", out var size) ? size : null,
            };
        }

        return info;
    }

    /// <summary>"sha256:ABC123" → "ABC123"；其他格式原样返回。</summary>
    internal static string? ExtractSha256(string? digest) => digest is null
        ? null
        : digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? digest["sha256:".Length..].Trim().ToUpperInvariant()
            : digest.Trim().ToUpperInvariant();

    internal static string? ExtractGiteeExeSha256(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var match = GiteeExeSha256Pattern.Match(notes);
        return match.Success ? match.Groups["hash"].Value.ToUpperInvariant() : null;
    }
}

internal static class JsonElementExtensions
{
    public static string? GetString(this JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static bool TryGetInt64(this JsonElement element, string name, out long value)
    {
        if (element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }
}
