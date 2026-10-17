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
            LogService.OpenCurrentLog();
        }
        catch (Exception ex)
        {
            LogService.Warn("打开诊断日志失败", ex);
            MessageBox.Show(
                $"无法直接打开诊断日志：{ex.Message}\n\n文件位置：{LogService.CurrentLogPath}",
                "电脑开荒",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
