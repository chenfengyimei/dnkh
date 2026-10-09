using System.Runtime.InteropServices;

namespace Dnkhw.Core.Downloads;

/// <summary>
/// Windows 已知文件夹与 Shell 通知相关 P/Invoke。
/// </summary>
public static class KnownFolders
{
    /// <summary>下载文件夹（Downloads）的 KNOWNFOLDERID。</summary>
    public static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    private const uint ShcneUpdatedir = 0x00001000;
    private const uint ShcnfPathW = 0x0005;
    private const uint WmSettingChange = 0x001A;
    private static readonly IntPtr HwndBroadcast = new(0xffff);
    private const uint SmtoAbortIfHung = 0x0002;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint flags, IntPtr token, out IntPtr path);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint fuFlags, uint timeout, out UIntPtr result);

    /// <summary>获取当前「下载」文件夹路径。</summary>
    public static string GetDownloadsPath()
    {
        var guid = DownloadsId;
        var hr = SHGetKnownFolderPath(ref guid, 0, IntPtr.Zero, out var ptr);
        if (hr != 0)
        {
            throw new InvalidOperationException($"SHGetKnownFolderPath 失败：0x{hr:X8}");
        }

        try
        {
            return Marshal.PtrToStringUni(ptr) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    /// <summary>通知 Explorer 目录结构已变化（迁移后刷新「下载」库指向）。</summary>
    public static void NotifyFolderChanged(string oldPath, string newPath)
    {
        var oldPtr = Marshal.StringToHGlobalUni(oldPath);
        var newPtr = Marshal.StringToHGlobalUni(newPath);
        try
        {
            SHChangeNotify(ShcneUpdatedir, ShcnfPathW, oldPtr, IntPtr.Zero);
            SHChangeNotify(ShcneUpdatedir, ShcnfPathW, newPtr, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(oldPtr);
            Marshal.FreeHGlobal(newPtr);
        }

        // 广播 User Shell Folders 变更，让运行中的应用重新解析已知文件夹
        _ = SendMessageTimeout(HwndBroadcast, WmSettingChange, UIntPtr.Zero, "User Shell Folders", SmtoAbortIfHung, 5000, out _);
    }
}
