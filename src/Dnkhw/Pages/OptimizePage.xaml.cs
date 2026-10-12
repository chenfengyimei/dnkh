using System.Windows;
using System.Windows.Controls;
using Dnkhw.Services;
using Dnkhw.ViewModels;

namespace Dnkhw.Pages;

public partial class OptimizePage : Page
{
    private readonly OptimizeViewModel _viewModel = new();

    public OptimizePage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Groups.Count == 0 && !_viewModel.IsBusy)
        {
            SafeFireAndForget.Run("优化项加载", () => _viewModel.LoadCommand.ExecuteAsync(null));
        }
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OptimizeRowView row)
        {
            SafeFireAndForget.Run("优化项切换", () => _viewModel.ToggleAsync(row));
        }
    }
}
