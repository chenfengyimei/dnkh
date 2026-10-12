using Dnkhw.Core.Settings;

namespace Dnkhw.Services;

/// <summary>进程内设置缓存：启动加载一次，变更后显式 Save 落盘。</summary>
public static class AppSettingsService
{
    public static AppSettings Current { get; private set; } = SettingsStore.Load();

    public static void Save()
    {
        SettingsStore.Save(Current);
    }
}
