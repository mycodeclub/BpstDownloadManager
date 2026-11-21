using Bdm.App.Services;
using Bdm.App.ViewModels;
using Bdm.Core;

namespace Bdm.App.Views;

public partial class DownloadsPage : ContentPage
{
    private readonly DownloadsViewModel _vm;
    private readonly IDispatcherTimer _timer;

    public DownloadsPage(DownloadsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(700);
        _timer.Tick += (_, _) => _vm.Tick();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Tick();
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        _timer.Stop();
        base.OnDisappearing();
    }

    private async void Add_Clicked(object? sender, EventArgs e)
    {
        var text = QuickAddEntry.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            // Nothing typed: use a link from the clipboard if there is one.
            try { text = UrlClassifier.ExtractUrl(await Clipboard.Default.GetTextAsync()); } catch (Exception) { }
        }
        QuickAddEntry.Text = "";
        await AppNavigation.OpenAddAsync(text);
    }

    private void QuickAdd_Completed(object? sender, EventArgs e) => Add_Clicked(sender, e);

    private async void TorrentFile_Clicked(object? sender, EventArgs e)
    {
        var torrentType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.WinUI] = [".torrent"],
            [DevicePlatform.Android] = ["application/x-bittorrent", "application/octet-stream"],
            [DevicePlatform.iOS] = ["public.data"],
        });
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a .torrent file", FileTypes = torrentType });
        if (file is null) return;
        await AppNavigation.OpenAddAsync(null, file.FullPath);
    }

    private async void Credit_Tapped(object? sender, TappedEventArgs e) =>
        await Launcher.Default.OpenAsync(Branding.WebsiteUrl);
}
