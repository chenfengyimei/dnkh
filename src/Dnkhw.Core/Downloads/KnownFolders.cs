using System.Runtime.InteropServices;

namespace Dnkhw.Core.Downloads;

/// <summary>
/// Windows 已知文件夹与 Shell 通知相关 P/Invoke。
/// </summary>
public static class KnownFolders
{
    /// <summary>桌面（Desktop）的 KNOWNFOLDERID。</summary>
    public static readonly Guid DesktopId = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");

    /// <summary>文档（Documents）的 KNOWNFOLDERID。</summary>
    public static readonly Guid DocumentsId = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");

    /// <summary>下载（Downloads）的 KNOWNFOLDERID。</summary>
    public static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>音乐（Music）的 KNOWNFOLDERID。</summary>
    public static readonly Guid MusicId = new("4BD8D571-6D19-48D3-BE97-422220050E73");

    /// <summary>图片（Pictures）的 KNOWNFOLDERID。</summary>
    public static readonly Guid PicturesId = new("33E28130-4E1E-4676-835A-98395C3BC3BB");

    /// <summary>视频（Videos）的 KNOWNFOLDERID。</summary>
    public static readonly Guid VideosId = new("18989B1D-99B5-455B-841C-AB7C74F423FC");

    private const uint ShcneUpdatedir = 0x00001000;
    private const uint ShcneAssocChanged = 0x08000000;
    private const uint ShcnfPathW = 0x0005;
    private const uint WmSettingChange = 0x001A;
    private static readonly IntPtr HwndBroadcast = new(0xffff);
    private const uint SmtoAbortIfHung = 0x0002;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint flags, IntPtr token, out IntPtr path);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHSetKnownFolderPath(ref Guid rfid, uint flags, IntPtr token, string path);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint fuFlags, uint timeout, out UIntPtr result);

    /// <summary>
    /// 读取任意已知文件夹的当前路径（按 KNOWNFOLDERID；缺失时按 Windows 标准行为自动创建）。
    /// 部分被重定向过的文件夹（如实测的音乐/视频）SHGetKnownFolderPath 可能返回 0x80070002，
    /// 此时回退 Environment.SpecialFolder（.NET 托管实现可正常解析）。
    /// </summary>
    public static string GetKnownFolderPath(Guid knownFolderId)
    {
        var guid = knownFolderId;
        const uint KfFlagCreate = 0x00008000; // 音乐/视频等文件夹可能被用户删除，缺失时创建
        var hr = SHGetKnownFolderPath(ref guid, KfFlagCreate, IntPtr.Zero, out var ptr);
        if (hr == 0)
        {
            try
            {
                var path = Marshal.PtrToStringUni(ptr);
                if (!string.IsNullOrEmpty(path))
                {
                    return path;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(ptr);
            }
        }

        // 回退：Environment.SpecialFolder 对被重定向的文件夹解析更稳
        var fallback = knownFolderId == DesktopId ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            : knownFolderId == DocumentsId ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : knownFolderId == MusicId ? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
            : knownFolderId == PicturesId ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            : knownFolderId == VideosId ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            : knownFolderId == DownloadsId
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                : string.Empty;

        if (!string.IsNullOrEmpty(fallback))
        {
            return fallback;
        }

        throw new InvalidOperationException($"SHGetKnownFolderPath 失败：0x{hr:X8}");
    }

    /// <summary>获取当前「下载」文件夹路径（兼容旧调用）。</summary>
    public static string GetDownloadsPath() => GetKnownFolderPath(DownloadsId);

    /// <summary>
    /// 通过 Windows 已知文件夹 API 重定向路径。相比仅修改注册表，这会同时更新 Shell 的
    /// 已知文件夹配置，避免属性页已变而资源管理器导航项仍解析到旧位置。
    /// </summary>
    public static void SetKnownFolderPath(Guid knownFolderId, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var folderId = knownFolderId;
        var hr = SHSetKnownFolderPath(ref folderId, 0, IntPtr.Zero, fullPath);
        if (hr != 0)
        {
            throw new InvalidOperationException(
                $"Windows 无法重定向已知文件夹（0x{unchecked((uint)hr):X8}）：{fullPath}");
        }
    }

    /// <summary>通知 Explorer 目录结构已变化（迁移后刷新文件夹库指向）。</summary>
    public static void NotifyFolderChanged(string oldPath, string newPath)
    {
        var oldPtr = Marshal.StringToHGlobalUni(oldPath);
        var newPtr = Marshal.StringToHGlobalUni(newPath);
        try
        {
            SHChangeNotify(ShcneUpdatedir, ShcnfPathW, oldPtr, IntPtr.Zero);
            SHChangeNotify(ShcneUpdatedir, ShcnfPathW, newPtr, IntPtr.Zero);
            // 让资源管理器失效已缓存的命名空间/图标解析结果。
            SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
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
