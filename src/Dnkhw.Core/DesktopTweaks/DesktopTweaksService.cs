using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Dnkhw.Core.DesktopTweaks;

/// <summary>
/// 桌面美化快捷开关：隐藏桌面图标、任务栏自动隐藏。
/// 均为官方公开机制，随时可逆。
/// </summary>
public static class DesktopTweaksService
{
    private const uint ShcneAllEvents = 0x7FFFFFFF;
    private const uint AbmGetState = 0x0004;
    private const uint AbmSetState = 0x000A;
    private const int AbsAutoHide = 0x0001;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(uint dwMessage, ref AppBarData pData);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int CbSize;
        public IntPtr HWnd;
        public uint UCallbackMessage;
        public uint UEdge;
        public Rect Rc;
        public IntPtr LParam;
    }

    #region 隐藏桌面图标

    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    /// <summary>桌面图标当前是否处于隐藏状态。</summary>
    public static bool GetDesktopIconsHidden()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey);
            return key?.GetValue("HideIcons") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>隐藏/显示桌面全部图标（系统级「查看 > 显示桌面图标」同一机制），并通知桌面刷新。</summary>
    public static void SetDesktopIconsHidden(bool hidden)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(AdvancedKey))
        {
            key.SetValue("HideIcons", hidden ? 1 : 0, RegistryValueKind.DWord);
        }

        // 广播全部变更事件让桌面立即刷新
        SHChangeNotify(ShcneAllEvents, 0, IntPtr.Zero, IntPtr.Zero);
    }

    #endregion

    #region 任务栏自动隐藏

    /// <summary>任务栏当前是否自动隐藏（SHAppBarMessage ABM_GETSTATE）。</summary>
    public static bool GetTaskbarAutoHide()
    {
        var data = new AppBarData { CbSize = Marshal.SizeOf<AppBarData>() };
        var result = SHAppBarMessage(AbmGetState, ref data);
        return ((uint)result.ToInt64() & AbsAutoHide) != 0;
    }

    /// <summary>设置任务栏自动隐藏（SHAppBarMessage ABM_SETSTATE，立即生效）。</summary>
    public static void SetTaskbarAutoHide(bool autoHide)
    {
        var data = new AppBarData
        {
            CbSize = Marshal.SizeOf<AppBarData>(),
            LParam = (IntPtr)AbsAutoHide * (autoHide ? 1 : 0),
        };
        SHAppBarMessage(AbmSetState, ref data);
    }

    #endregion
}
