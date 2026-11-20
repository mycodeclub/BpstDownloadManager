using System.Diagnostics;
using System.Windows;
using BpstVideoDownloader.Core;

namespace BpstVideoDownloader.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {Branding.Version}";
    }

    public static void OpenWebsite() =>
        Process.Start(new ProcessStartInfo(Branding.WebsiteUrl) { UseShellExecute = true });

    private void Visit_Click(object sender, RoutedEventArgs e) => OpenWebsite();

    private void Link_Navigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Website_Navigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        OpenWebsite();
        e.Handled = true;
    }
}
