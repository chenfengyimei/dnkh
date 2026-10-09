using System.Windows;
using System.Windows.Controls;
using Dnkhw.ViewModels;

namespace Dnkhw.Pages;

public partial class AppsPage : Page
{
    private readonly AppsViewModel _viewModel = new();

    public AppsPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Categories.Count == 0 && !_viewModel.IsBusy)
        {
            _ = _viewModel.LoadCommand.ExecuteAsync(null);
        }
    }
}
