using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Dnkhw.Core.Wallpaper;

/// <summary>
/// 设置/读取桌面壁纸：SystemParametersInfo(SPI_SETDESKWALLPAPER) + 注册表 WallpaperStyle/TileWallpaper。
/// </summary>
public static class WallpaperService
{
    private const uint SpiSetDeskWallpaper = 0x0014;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

    /// <summary>设置桌面壁纸（jpg/png/bmp）并写入填充模式。</summary>
    public static void Set(string path, WallpaperFit fit)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("壁纸文件不存在", path);
        }

        // 填充模式映射：WallpaperStyle 0=平铺/居中, 2=拉伸, 6=适应, 10=填充；TileWallpaper 1=平铺
        var (style, tile) = fit switch
        {
            WallpaperFit.Fill => ("10", "0"),
            WallpaperFit.Fit => ("6", "0"),
            WallpaperFit.Stretch => ("2", "0"),
            WallpaperFit.Tile => ("0", "1"),
            WallpaperFit.Center => ("0", "0"),
            _ => ("10", "0"),
        };

        using (var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true))
        {
            if (desktop is null)
            {
                throw new InvalidOperationException("无法打开注册表 Control Panel\\Desktop");
            }

            desktop.SetValue("WallpaperStyle", style, RegistryValueKind.String);
            desktop.SetValue("TileWallpaper", tile, RegistryValueKind.String);
        }

        if (!SystemParametersInfo(SpiSetDeskWallpaper, 0, path, SpifUpdateIniFile | SpifSendChange))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "设置壁纸失败");
        }
    }

    /// <summary>读取当前壁纸文件路径。</summary>
    public static string GetCurrent()
    {
        using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        return desktop?.GetValue("Wallpaper") as string ?? string.Empty;
    }
}
