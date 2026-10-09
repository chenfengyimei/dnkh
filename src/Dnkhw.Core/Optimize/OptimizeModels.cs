namespace Dnkhw.Core.Optimize;

public sealed record OptimizeItem(
    string Id,
    string Title,
    string Description,
    bool NeedsAdmin,
    string GroupId = "",
    string GroupName = "");

/// <summary>优化项分组（广告与推广/搜索体验/通知与建议/隐私与遥测/性能与游戏）。</summary>
public sealed record OptimizeGroup(string Id, string Name, IReadOnlyList<OptimizeItem> Items);
