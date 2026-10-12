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
            return settings ?? new AppSettings();
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

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, path, overwrite: true);
    }
}
