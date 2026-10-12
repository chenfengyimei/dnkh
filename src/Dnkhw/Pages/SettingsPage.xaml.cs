using System.Windows;
using System.Windows.Controls;
using Dnkhw.Services;
using Dnkhw.ViewModels;

namespace Dnkhw.Pages;

public partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel = new();

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            LogService.OpenInExplorer();
        }
        catch (Exception ex)
        {
            LogService.Warn("打开日志文件夹失败", ex);
        }
    }
}
