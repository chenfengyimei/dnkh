using CommunityToolkit.Mvvm.ComponentModel;
using Dnkhw.Core.Settings;
using Dnkhw.Services;

namespace Dnkhw.ViewModels;

/// <summary>
/// 设置页：主题三态（跟随系统/浅色/深色）+ 记住上次页面。
/// 变更即时生效并落盘（原子写 %APPDATA%\dnkhw\settings.json）。
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private AppTheme _theme;

    public SettingsViewModel()
    {
        _theme = AppSettingsService.Current.Theme;
        _rememberLastPage = AppSettingsService.Current.RememberLastPage;
    }

    public AppTheme Theme => _theme;

    public bool IsFollow
    {
        get => _theme == AppTheme.Follow;
        set => SelectTheme(AppTheme.Follow, value);
    }

    public bool IsLight
    {
        get => _theme == AppTheme.Light;
        set => SelectTheme(AppTheme.Light, value);
    }

    public bool IsDark
    {
        get => _theme == AppTheme.Dark;
        set => SelectTheme(AppTheme.Dark, value);
    }

    private void SelectTheme(AppTheme theme, bool isChecked)
    {
        if (!isChecked || _theme == theme)
        {
            return;
        }

        _theme = theme;
        OnPropertyChanged(nameof(IsFollow));
        OnPropertyChanged(nameof(IsLight));
        OnPropertyChanged(nameof(IsDark));

        var settings = AppSettingsService.Current;
        settings.Theme = theme;
        SaveQuietly();
        ThemeCoordinator.Apply(theme);
    }

    [ObservableProperty]
    private bool _rememberLastPage;

    partial void OnRememberLastPageChanged(bool value)
    {
        AppSettingsService.Current.RememberLastPage = value;
        SaveQuietly();
    }

    private static void SaveQuietly()
    {
        try
        {
            AppSettingsService.Save();
        }
        catch (Exception ex)
        {
            LogService.Warn("保存设置失败", ex);
        }
    }
}
