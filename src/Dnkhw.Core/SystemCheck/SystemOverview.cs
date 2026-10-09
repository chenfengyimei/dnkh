namespace Dnkhw.Core.SystemCheck;

public sealed record SystemOverview(
    string OsName,
    string Edition,
    string DisplayVersion,
    string Build,
    string Cpu,
    string Gpu,
    long RamBytes,
    string Resolution,
    string HostName,
    string Uptime,
    IReadOnlyList<DiskInfo> Disks);
