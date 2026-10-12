using System.Windows;
using System.Windows.Controls;
using Dnkhw.Services;
using Dnkhw.ViewModels;

namespace Dnkhw.Pages;

public partial class WallpaperPage : Page
{
    private readonly WallpaperViewModel _viewModel = new();

    public WallpaperPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnIconsToggleClick(object sender, RoutedEventArgs e)
    {
        SafeFireAndForget.Run("桌面图标开关", () => _viewModel.ToggleIconsAsync());
    }

    private void OnTaskbarToggleClick(object sender, RoutedEventArgs e)
    {
        SafeFireAndForget.Run("任务栏自动隐藏开关", () => _viewModel.ToggleTaskbarAsync());
    }
}
