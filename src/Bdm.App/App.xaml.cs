using Bdm.App.Services;
using Bdm.Core;

namespace Bdm.App;

public partial class App : Application
{
    private readonly DownloadManager _manager;
    private readonly IPlatformServices _platform;
    private string? _lastClipboard;
    private DateTime _lastClipboardAt;

    public App(DownloadManager manager, IPlatformServices platform)
    {
        InitializeComponent();
        _manager = manager;
        _platform = platform;

        _manager.BusyChanged += busy => _platform.SetBusy(busy && _manager.Settings.KeepAwake || busy && DeviceInfo.Platform == DevicePlatform.Android);
        _manager.JobFinished += job => _ = OnFinishedAsync(job);
        Clipboard.Default.ClipboardContentChanged += OnClipboardChanged;
        _ = _manager.AutoUpdateYtDlpAsync();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell()) { Title = $"{Branding.Product} 2.0" };
        if (DeviceInfo.Platform == DevicePlatform.WinUI)
        {
            window.Width = 1180;
            window.Height = 760;
            window.MinimumWidth = 720;
            window.MinimumHeight = 480;
        }
        window.Created += async (_, _) =>
        {
            await _platform.RequestPermissionsAsync();
            if (PendingIncoming is { } pending)
            {
                PendingIncoming = null;
                await Task.Delay(500);
                await AppNavigation.HandleIncomingAsync(pending.Text, pending.TorrentPath);
            }
        };
        return window;
    }

    /// <summary>A link or .torrent received before the window existed (share intent, command line).</summary>
    public static (string? Text, string? TorrentPath)? PendingIncoming { get; set; }

    private async Task OnFinishedAsync(DownloadJob job)
    {
        try
        {
            await _platform.OnDownloadFinishedAsync(job);
        }
        catch (Exception ex)
        {
            AppLog.Error("Finish handling failed", ex);
        }
    }

    private async void OnClipboardChanged(object? sender, EventArgs e)
    {
        // Desktop: copying a link anywhere opens the Add page (like classic download managers).
        if (!_manager.Settings.MonitorClipboard || DeviceInfo.Idiom != DeviceIdiom.Desktop) return;
        try
        {
            var text = await Clipboard.Default.GetTextAsync();
            var url = UrlClassifier.ExtractUrl(text);
            if (url is null || !UrlClassifier.IsCatchable(url)) return;
            if (url == _lastClipboard && DateTime.UtcNow - _lastClipboardAt < TimeSpan.FromSeconds(10)) return;
            _lastClipboard = url;
            _lastClipboardAt = DateTime.UtcNow;
            await AppNavigation.OpenAddAsync(url, fromClipboard: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("Clipboard read failed", ex);
        }
    }
}
