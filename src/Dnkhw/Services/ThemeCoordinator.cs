using System.Windows;
using Dnkhw.Core.Settings;
using Wpf.Ui.Appearance;

namespace Dnkhw.Services;

/// <summary>
/// 主题三态协调：跟随系统 = SystemThemeWatcher 监听；固定亮/暗 = 取消监听后应用指定主题。
/// 由主窗口 Attach 注册宿主，供设置页运行时切换。
/// </summary>
public static class ThemeCoordinator
{
    private static Window? _mainWindow;

    public static void Attach(Window mainWindow)
    {
        _mainWindow = mainWindow;
        Apply(AppSettingsService.Current.Theme);
    }

    public static void Apply(AppTheme theme)
    {
        try
        {
            switch (theme)
            {
                case AppTheme.Light:
                    if (_mainWindow is not null)
                    {
                        SystemThemeWatcher.UnWatch(_mainWindow);
                    }

                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    break;
                case AppTheme.Dark:
                    if (_mainWindow is not null)
                    {
                        SystemThemeWatcher.UnWatch(_mainWindow);
                    }

                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    break;
                default:
                    if (_mainWindow is not null)
                    {
                        SystemThemeWatcher.Watch(_mainWindow);
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("应用主题切换失败", ex);
        }
    }
}
