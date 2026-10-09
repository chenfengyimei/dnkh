namespace Dnkhw.Core.AppCatalog;

public sealed record AppEntry(
    string Name,
    string Description,
    string Homepage,
    string? WingetId,
    bool Recommend = false);

public sealed record AppCategory(string Id, string Name, IReadOnlyList<AppEntry> Apps);

public sealed record AppData(IReadOnlyList<AppCategory> Categories);
