using System.Windows;
using System.Windows.Controls;
using Dnkhw.ViewModels;

namespace Dnkhw.Pages;

public partial class DefaultPathsPage : Page
{
    private readonly DefaultPathsViewModel _viewModel = new();

    public DefaultPathsPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Folders.Count == 0 && !_viewModel.IsBusy)
        {
            _ = _viewModel.LoadCommand.ExecuteAsync(null);
        }
    }
}
