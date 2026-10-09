using System.Security.Principal;
using System.Windows;
using Dnkhw.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Dnkhw;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this);
        UpdateAdminBadge();
    }

    private void RootNavigation_Loaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.SetPageProviderService(new PageProviderService());
        RootNavigation.Navigate(typeof(Pages.CheckPage));
    }

    private void UpdateAdminBadge()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        bool isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        AdminBadge.Content = isAdmin
            ? "管理员权限"
            : "普通权限（部分功能需提权）";
    }
}
