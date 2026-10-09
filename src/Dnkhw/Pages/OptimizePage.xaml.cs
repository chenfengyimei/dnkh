using System.Windows;
using System.Windows.Controls;
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
        if (_viewModel.Items.Count == 0 && !_viewModel.IsBusy)
        {
            _ = _viewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OptimizeRowView row)
        {
            _ = _viewModel.ToggleAsync(row);
        }
    }
}
