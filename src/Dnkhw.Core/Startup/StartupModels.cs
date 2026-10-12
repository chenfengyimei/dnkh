namespace Dnkhw.Core.Startup;

public enum StartupLocation
{
    /// <summary>HKCU\...\CurrentVersion\Run</summary>
    RunUser,

    /// <summary>HKLM\...\CurrentVersion\Run（需管理员）</summary>
    RunMachine,

    /// <summary>HKLM 32 位视图 ...\CurrentVersion\Run（需管理员）</summary>
    RunMachine32,

    /// <summary>用户启动文件夹（%APPDATA%\...\Startup）</summary>
    FolderUser,

    /// <summary>公共启动文件夹（%ProgramData%\...\StartUp，需管理员）</summary>
    FolderCommon,
}

public sealed record StartupItem(
    string Name,
    string RawName,
    string Command,
    StartupLocation Location,
    bool IsEnabled,
    bool NeedsAdmin,
    string? Publisher)
{
    public string LocationText => Location switch
    {
        StartupLocation.RunUser => "用户 Run 键",
        StartupLocation.RunMachine => "系统 Run 键（需管理员）",
        StartupLocation.RunMachine32 => "系统 Run 键（32 位，需管理员）",
        StartupLocation.FolderUser => "用户启动文件夹",
        StartupLocation.FolderCommon => "公共启动文件夹（需管理员）",
        _ => string.Empty,
    };
}
