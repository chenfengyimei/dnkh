namespace Dnkhw.Core.Update;

public enum UpdateSourceKind
{
    /// <summary>GitHub Releases（latest）——资产带 sha256 digest，可在线自更新。</summary>
    GitHubRelease,

    /// <summary>Gitee Releases（latest）——API 不带哈希，只引导下载页。</summary>
    GiteeRelease,

    /// <summary>官网 version.json——永远可用，带 sha256 可在线自更新。</summary>
    VersionJson,
}

/// <summary>一个更新源：类型 + 数据接口地址。</summary>
public sealed record UpdateSource(UpdateSourceKind Kind, string Url);

/// <summary>
/// 更新源配置（多级回落：git 仓库 → 官网）。
/// </summary>
public static class UpdateSources
{
    /// <summary>
    /// 开源仓库占位符——仓库建立后改这两处即可（格式：owner/repo）。
    /// git 源不可用时自动回落官网 version.json，功能不受影响。
    /// </summary>
    public const string GitHubRepo = "owner/dnkhw";

    /// <summary>Gitee 仓库（国内访问快），占位符同上。</summary>
    public const string GiteeRepo = "owner/dnkhw";

    /// <summary>Release 资产文件名（与发布时的附件名一致）。</summary>
    public const string AssetFileName = "Dnkhw.exe";

    /// <summary>
    /// 开发/自测覆盖：设置 DNKHW_UPDATE_JSON_URL 后，官网源改用该地址
    /// （git 源不变，仍在前面试探后回落）。不设置时行为与发布版完全一致。
    /// </summary>
    public static string? EnvOverrideVersionJson =>
        Environment.GetEnvironmentVariable("DNKHW_UPDATE_JSON_URL") is { Length: > 0 } url ? url : null;

    /// <summary>按回落顺序排列的默认更新源。</summary>
    public static IReadOnlyList<UpdateSource> Defaults =>
    [
        new UpdateSource(UpdateSourceKind.GitHubRelease, GitHubLatestApiUrl),
        new UpdateSource(UpdateSourceKind.GiteeRelease, GiteeLatestApiUrl),
        new UpdateSource(UpdateSourceKind.VersionJson, EnvOverrideVersionJson ?? ProductLinks.VersionJson),
    ];

    internal static string GitHubLatestApiUrl => $"https://api.github.com/repos/{GitHubRepo}/releases/latest";

    internal static string GiteeLatestApiUrl => $"https://gitee.com/api/v5/repos/{GiteeRepo}/releases/latest";
}
