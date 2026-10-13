using System.IO;
using System.Text.Json;

namespace Dnkhw.Core.Settings;

/// <summary>主题偏好：跟随系统 / 固定浅色 / 固定深色。</summary>
public enum AppTheme
{
    Follow = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>用户偏好（JSON 持久化，字段新增时保持默认值兼容旧文件）。</summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Follow;

    /// <summary>是否记住上次访问的页面，下次启动直达。</summary>
    public bool RememberLastPage { get; set; } = true;

    /// <summary>上次访问的页面 key（check/wallpaper/…/settings）。</summary>
    public string? LastPageKey { get; set; }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "dnkhw",
        "settings.json");

    /// <summary>读取设置；文件缺失或损坏时返回默认值（下次保存时自动修复）。</summary>
    public static AppSettings Load(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
            return Normalize(settings ?? new AppSettings());
        }
        catch
        {
            // JSON 损坏、占用等任何异常都回退默认值，绝不能挡住启动
            return new AppSettings();
        }
    }

    /// <summary>原子写入：先写临时文件再整体替换，写一半崩溃不产生残缺设置文件。</summary>
    public static void Save(AppSettings settings, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        path ??= DefaultPath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 使用唯一临时文件，避免快速连续点击设置时两个写入互相覆盖临时文件。
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(Normalize(settings), JsonOptions));
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // 原子替换成功后临时文件已不存在；清理失败不掩盖原始保存异常。
            }
        }
    }

    /// <summary>把磁盘上可被手工编辑的设置收敛到受支持范围，避免异常枚举值破坏界面状态。</summary>
    private static AppSettings Normalize(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.Theme))
        {
            settings.Theme = AppTheme.Follow;
        }

        if (settings.LastPageKey is { } key)
        {
            // 页面 key 只用于启动导航；限制长度与控制字符，未知值仍由 UI 安全回退到首页。
            var normalized = key.Trim();
            settings.LastPageKey = normalized.Length is 0 or > 64
                || normalized.Any(char.IsControl)
                ? null
                : normalized;
        }

        return settings;
    }
}
