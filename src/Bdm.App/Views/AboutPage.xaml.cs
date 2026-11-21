using Bdm.Core;

namespace Bdm.App.Views;

public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();
        VersionLabel.Text = $"Version {AppInfo.Current.VersionString} · {DeviceInfo.Current.Platform}";
    }

    private async void Website_Tapped(object? sender, EventArgs e) => await Launcher.Default.OpenAsync(Branding.WebsiteUrl);
    private async void Source_Clicked(object? sender, EventArgs e) => await Launcher.Default.OpenAsync(Branding.SourceUrl);
    private async void Project_Clicked(object? sender, EventArgs e) => await Launcher.Default.OpenAsync(Branding.ProjectPageUrl);
}
