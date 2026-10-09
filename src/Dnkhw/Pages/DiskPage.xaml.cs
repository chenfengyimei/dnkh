using System.Windows;
using System.Windows.Controls;
using Dnkhw.ViewModels;

namespace Dnkhw.Pages;

public partial class DiskPage : Page
{
    private readonly DiskViewModel _viewModel = new();

    public DiskPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Drives.Count == 0)
        {
            _viewModel.LoadCommand.Execute(null);
        }
    }
}
