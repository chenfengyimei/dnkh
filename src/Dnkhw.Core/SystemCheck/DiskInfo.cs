namespace Dnkhw.Core.SystemCheck;

public sealed record DiskInfo(string Root, string Label, long TotalBytes, long FreeBytes);
