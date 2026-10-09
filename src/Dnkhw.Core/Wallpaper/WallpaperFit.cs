namespace Dnkhw.Core.Wallpaper;

/// <summary>壁纸填充模式，对应注册表 WallpaperStyle/TileWallpaper 组合。</summary>
public enum WallpaperFit
{
    /// <summary>填充（放大裁切铺满屏幕）</summary>
    Fill,

    /// <summary>适应（完整显示，可能留边）</summary>
    Fit,

    /// <summary>拉伸（强制拉伸至屏幕尺寸）</summary>
    Stretch,

    /// <summary>平铺（按原始尺寸重复排列）</summary>
    Tile,

    /// <summary>居中（按原始尺寸居中显示）</summary>
    Center,
}
