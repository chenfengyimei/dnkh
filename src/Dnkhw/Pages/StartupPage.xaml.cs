using System.Windows;
using System.Windows.Controls;
using Dnkhw.Services;
using Dnkhw.ViewModels;

namespace Dnkhw.Pages;

public partial class StartupPage : Page
{
    private readonly StartupViewModel _viewModel = new();

    public StartupPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Items.Count == 0 && !_viewModel.IsBusy)
        {
            SafeFireAndForget.Run("启动项加载", () => _viewModel.LoadCommand.ExecuteAsync(null));
        }
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is StartupItemView row)
        {
            SafeFireAndForget.Run("启动项切换", () => _viewModel.ToggleAsync(row));
        }
    }
}
