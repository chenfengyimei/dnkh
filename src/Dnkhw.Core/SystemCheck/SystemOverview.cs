namespace Dnkhw.Core.SystemCheck;

public sealed record SystemOverview(
    string OsName,
    string Edition,
    string DisplayVersion,
    string Build,
    string ComputerType,
    string SystemModel,
    string BaseBoard,
    string BiosInfo,
    string Cpu,
    string Gpu,
    long RamBytes,
    string Resolution,
    string HostName,
    string Uptime,
    string InstallDate,
    IReadOnlyList<DiskInfo> Disks);
