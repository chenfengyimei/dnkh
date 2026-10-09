using System.Reflection;
using System.Windows.Controls;

namespace Dnkhw.Pages;

public partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "版本 0.1.0" : $"版本 {version.Major}.{version.Minor}.{version.Build}";
    }
}
