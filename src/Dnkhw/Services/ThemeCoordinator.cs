using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Dnkhw.Core.Settings;
using Wpf.Ui.Appearance;

namespace Dnkhw.Services;

/// <summary>
/// 主题三态协调：跟随系统 = 监听 Windows 外观消息；固定亮/暗 = 取消监听后应用指定主题。
/// 由主窗口 Attach 注册宿主，供设置页运行时切换。
/// </summary>
public static class ThemeCoordinator
{
    // WPF-UI 4.3.0 的 SystemThemeWatcher 在窗口已 Loaded 时不会立即同步主题，
    // 且部分 Windows 版本只发送 WM_SETTINGCHANGE，导致运行时切换漏报。
    // 在主窗口上直接监听这些原生消息，避免依赖上游监听器的版本行为。
    private const int WmSettingChange = 0x001A;
    private const int WmSysColorChange = 0x0015;
    private const int WmThemeChanged = 0x031A;
    private const int WmDwmColorizationColorChanged = 0x0320;

    private static Window? _mainWindow;
    private static HwndSource? _windowSource;
    private static bool _isFollowingSystem;
    private static bool _systemSyncQueued;

    public static void Attach(Window mainWindow)
    {
        if (!ReferenceEquals(_mainWindow, mainWindow))
        {
            DetachWindowMessageHook();
            if (_mainWindow is not null)
            {
                _mainWindow.Closed -= OnMainWindowClosed;
            }
        }

        _mainWindow = mainWindow;
        _mainWindow.Closed -= OnMainWindowClosed;
        _mainWindow.Closed += OnMainWindowClosed;
        Apply(AppSettingsService.Current.Theme);
    }

    public static void Apply(AppTheme theme)
    {
        try
        {
            switch (theme)
            {
                case AppTheme.Light:
                    StopFollowingSystem();
                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    break;
                case AppTheme.Dark:
                    StopFollowingSystem();
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    break;
                default:
                    StartFollowingSystem();
                    break;
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("应用主题切换失败", ex);
        }
    }

    private static void StartFollowingSystem()
    {
        _isFollowingSystem = true;
        AttachWindowMessageHook();

        // 注册监听后立即同步，确保从固定深/浅色切回“跟随系统”时当场生效。
        ApplicationThemeManager.ApplySystemTheme();
    }

    private static void StopFollowingSystem()
    {
        _isFollowingSystem = false;
        _systemSyncQueued = false;
        DetachWindowMessageHook();
    }

    private static void AttachWindowMessageHook()
    {
        if (_windowSource is not null || _mainWindow is null || !_mainWindow.IsLoaded)
        {
            return;
        }

        var handle = new WindowInteropHelper(_mainWindow).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(OnWindowMessage);
    }

    private static void DetachWindowMessageHook()
    {
        _windowSource?.RemoveHook(OnWindowMessage);
        _windowSource = null;
    }

    private static IntPtr OnWindowMessage(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (_isFollowingSystem && IsSystemAppearanceMessage(message))
        {
            QueueSystemThemeSync();
        }

        return IntPtr.Zero;
    }

    private static bool IsSystemAppearanceMessage(int message) => message is
        WmSettingChange or
        WmSysColorChange or
        WmThemeChanged or
        WmDwmColorizationColorChanged;

    private static void QueueSystemThemeSync()
    {
        var window = _mainWindow;
        if (_systemSyncQueued || window is null)
        {
            return;
        }

        // Windows 往往会连续广播多条外观消息；合并到一次 UI 线程刷新，避免重复换资源字典。
        _systemSyncQueued = true;
        _ = window.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                _systemSyncQueued = false;
                if (!_isFollowingSystem)
                {
                    return;
                }

                try
                {
                    ApplicationThemeManager.ApplySystemTheme();
                }
                catch (Exception ex)
                {
                    LogService.Warn("跟随系统主题刷新失败", ex);
                }
            }));
    }

    private static void OnMainWindowClosed(object? sender, EventArgs e)
    {
        StopFollowingSystem();
        if (_mainWindow is not null)
        {
            _mainWindow.Closed -= OnMainWindowClosed;
        }

        _mainWindow = null;
    }
}
