using System.IO;
using System.Windows;
using System.Windows.Controls;
using Dnkhw.Services;
using Dnkhw.ViewModels;
using Microsoft.Win32;

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
            SafeFireAndForget.Run("APP 清单加载", () => _viewModel.LoadCommand.ExecuteAsync(null));
        }
    }

    private void BrowseInstallRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择推荐应用的安装根目录",
            Multiselect = false,
        };

        if (!string.IsNullOrWhiteSpace(_viewModel.InstallRoot)
            && Directory.Exists(_viewModel.InstallRoot))
        {
            dialog.InitialDirectory = _viewModel.InstallRoot;
        }

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            try
            {
                _viewModel.SetInstallRoot(dialog.FolderName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"无法使用该安装位置：{ex.Message}",
                    "电脑开荒",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
