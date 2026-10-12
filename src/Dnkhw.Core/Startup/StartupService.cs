using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Dnkhw.Core.Startup;

/// <summary>
/// 开机启动项：枚举 Run 键与启动文件夹，读取/写入 StartupApproved 标记（任务管理器同机制，可逆）。
/// 注：RunOnce 不展示（任务管理器同样不列）。
/// </summary>
public sealed class StartupService
{
    private const string RunSubkey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRunSubkey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRun32Subkey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string ApprovedFolderSubkey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    public IReadOnlyList<StartupItem> GetItems()
    {
        var items = new List<StartupItem>();

        CollectRunItems(Registry.CurrentUser, Registry.CurrentUser, ApprovedRunSubkey, StartupLocation.RunUser, needsAdmin: false, items);
        CollectRunItems(Registry.LocalMachine, Registry.LocalMachine, ApprovedRunSubkey, StartupLocation.RunMachine, needsAdmin: true, items);
        if (Environment.Is64BitOperatingSystem)
        {
            using var machine32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            CollectRunItems(machine32, Registry.LocalMachine, ApprovedRun32Subkey, StartupLocation.RunMachine32, needsAdmin: true, items);
        }

        var userFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var commonFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        CollectFolderItems(userFolder, Registry.CurrentUser, StartupLocation.FolderUser, needsAdmin: false, items);
        CollectFolderItems(commonFolder, Registry.LocalMachine, StartupLocation.FolderCommon, needsAdmin: true, items);

        return items
            .OrderBy(i => i.Location)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>启用/禁用一个启动项。HKLM/公共目录项需要管理员权限，否则抛 UnauthorizedAccessException。</summary>
    public void SetEnabled(StartupItem item, bool enable)
    {
        var (hive, approvedPath) = item.Location switch
        {
            StartupLocation.RunUser => (Registry.CurrentUser, ApprovedRunSubkey),
            StartupLocation.RunMachine => (Registry.LocalMachine, ApprovedRunSubkey),
            StartupLocation.RunMachine32 => (Registry.LocalMachine, ApprovedRun32Subkey),
            StartupLocation.FolderUser => (Registry.CurrentUser, ApprovedFolderSubkey),
            StartupLocation.FolderCommon => (Registry.LocalMachine, ApprovedFolderSubkey),
            _ => throw new ArgumentOutOfRangeException(nameof(item)),
        };

        using var key = hive.CreateSubKey(approvedPath)
            ?? throw new InvalidOperationException("无法打开 StartupApproved 键");
        key.SetValue(item.RawName, StartupApprovedCodec.Encode(enable), RegistryValueKind.Binary);
    }

    private static void CollectRunItems(
        RegistryKey runHive,
        RegistryKey approvedHive,
        string approvedSubkey,
        StartupLocation location,
        bool needsAdmin,
        List<StartupItem> items)
    {
        try
        {
            using var key = runHive.OpenSubKey(RunSubkey);
            if (key is null)
            {
                return;
            }

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is not string command || string.IsNullOrWhiteSpace(command))
                {
                    continue;
                }

                items.Add(new StartupItem(
                    Name: name,
                    RawName: name,
                    Command: command,
                    Location: location,
                    IsEnabled: ReadApproved(approvedHive, approvedSubkey, name),
                    NeedsAdmin: needsAdmin,
                    Publisher: GetPublisher(CommandResolver.ResolveExecutable(command))));
            }
        }
        catch
        {
            // 单个来源枚举失败不影响其余来源
        }
    }

    private static void CollectFolderItems(
        string folder,
        RegistryKey approvedHive,
        StartupLocation location,
        bool needsAdmin,
        List<StartupItem> items)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var fileName = Path.GetFileName(file);
                if (string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // 文件夹系统配置，不是启动项
                }

                string command = file;
                if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    command = ResolveShortcutTarget(file) ?? file;
                }

                items.Add(new StartupItem(
                    Name: Path.GetFileNameWithoutExtension(fileName),
                    RawName: fileName,
                    Command: command,
                    Location: location,
                    IsEnabled: ReadApproved(approvedHive, ApprovedFolderSubkey, fileName),
                    NeedsAdmin: needsAdmin,
                    Publisher: GetPublisher(CommandResolver.ResolveExecutable(command))));
            }
        }
        catch
        {
            // 单个来源枚举失败不影响其余来源
        }
    }

    private static bool ReadApproved(RegistryKey hive, string subkeyPath, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(subkeyPath);
            return StartupApprovedCodec.Decode(key?.GetValue(name) as byte[]);
        }
        catch
        {
            return true;
        }
    }

    private static string? ResolveShortcutTarget(string lnkPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            shell = Activator.CreateInstance(shellType)!;
            dynamic dynamicShell = shell;
            shortcut = dynamicShell.CreateShortcut(lnkPath);
            dynamic dynamicShortcut = shortcut;
            return (string?)dynamicShortcut.TargetPath;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                _ = Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                _ = Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static string? GetPublisher(string? executablePath)
    {
        if (executablePath is null)
        {
            return null;
        }

        try
        {
            // 当前目标框架暂无更合适的签名文件证书加载替代 API，CreateFromSignedFile 仍可用（SYSLIB0057）
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(executablePath));
#pragma warning restore SYSLIB0057
            var name = cert.GetNameInfo(X509NameType.SimpleName, false);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null; // 未签名或读取失败
        }
    }
}
