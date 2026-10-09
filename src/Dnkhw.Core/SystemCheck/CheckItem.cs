namespace Dnkhw.Core.SystemCheck;

/// <summary>
/// 单项检查结果。FixPage 非空时，UI 提供「去处理」按钮跳转到对应页面。
/// </summary>
/// <param name="FixPage">目标页面标识（如 "downloads"、"optimize"）。</param>
public sealed record CheckItem(
    string Id,
    string Title,
    CheckStatus Status,
    string Detail,
    string? Suggestion = null,
    string? FixPage = null);
