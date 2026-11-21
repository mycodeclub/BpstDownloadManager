using Bdm.App.Views;
using Bdm.Core;

namespace Bdm.App.Services;

/// <summary>Opens pages from anywhere (clipboard capture, share intents, command line, buttons).</summary>
public static class AppNavigation
{
    public static IServiceProvider Services => IPlatformApplication.Current!.Services;

    public static T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    private static bool _addOpen;

    /// <summary>Opens the Add page for a link and/or a local .torrent file. Ignored if one is already open.</summary>
    public static async Task OpenAddAsync(string? url, string? torrentPath = null, bool fromClipboard = false)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (_addOpen) return;
            if (fromClipboard) Get<IPlatformServices>().BringToFront();
            _addOpen = true;
            var page = new AddDownloadPage(Get<DownloadManager>(), Get<IPlatformServices>(), url, torrentPath);
            page.Disappearing += (_, _) => _addOpen = false;
            await Shell.Current.Navigation.PushModalAsync(new NavigationPage(page));
        });
    }

    public static async Task OpenGrabberAsync(string? url)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await Shell.Current.GoToAsync("//grabber");
            if (Shell.Current.CurrentPage is GrabberPage g && !string.IsNullOrWhiteSpace(url)) g.Navigate(url);
        });
    }

    /// <summary>Text shared to the app, passed on the command line, or copied: route it to the right page.</summary>
    public static Task HandleIncomingAsync(string? text, string? torrentPath = null)
    {
        if (torrentPath is not null) return OpenAddAsync(null, torrentPath);
        var url = UrlClassifier.ExtractUrl(text) ?? text?.Trim();
        return string.IsNullOrWhiteSpace(url) ? Task.CompletedTask : OpenAddAsync(url);
    }
}
