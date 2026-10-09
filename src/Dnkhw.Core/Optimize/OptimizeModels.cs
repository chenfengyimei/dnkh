namespace Dnkhw.Core.Optimize;

public sealed record OptimizeItem(
    string Id,
    string Title,
    string Description,
    bool NeedsAdmin);
