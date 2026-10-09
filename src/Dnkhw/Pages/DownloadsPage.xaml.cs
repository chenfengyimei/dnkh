using System.Windows;
using System.Windows.Controls;
using Dnkhw.ViewModels;

namespace Dnkhw.Pages;

public partial class DownloadsPage : Page
{
    private readonly DownloadsViewModel _viewModel = new();

    public DownloadsPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Status is null && !_viewModel.IsBusy)
        {
            _ = _viewModel.LoadCommand.ExecuteAsync(null);
        }
    }
}
