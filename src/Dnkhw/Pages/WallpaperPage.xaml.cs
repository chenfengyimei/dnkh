using System.Windows;
using System.Windows.Controls;
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
        _ = _viewModel.ToggleIconsAsync();
    }

    private void OnTaskbarToggleClick(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.ToggleTaskbarAsync();
    }
}
