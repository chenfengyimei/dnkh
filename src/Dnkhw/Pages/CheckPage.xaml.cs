using System.Windows;
using System.Windows.Controls;
using Dnkhw.ViewModels;

namespace Dnkhw.Pages;

public partial class CheckPage : Page
{
    private readonly CheckViewModel _viewModel = new();

    public CheckPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 页面实例被缓存，仅在首次进入且无结果时自动体检一次
        if (_viewModel.Results.Count == 0 && !_viewModel.IsBusy)
        {
            _ = _viewModel.CheckCommand.ExecuteAsync(null);
        }
    }
}
