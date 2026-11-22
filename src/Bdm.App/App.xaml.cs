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
            if (StartupArgs.Contains("--self-test"))
            {
                await Task.Delay(800);
                Environment.Exit(await RunSelfTestAsync() ? 0 : 1);
                return;
            }
            if (StartupArgs.FirstOrDefault(a => a.StartsWith("--page=", StringComparison.Ordinal)) is { } pageArg)
            {
                await Task.Delay(500);
                var route = pageArg["--page=".Length..];
                if (route == "add") await AppNavigation.OpenAddAsync("https://www.youtube.com/watch?v=eRsGyueVLvQ");
                else await Shell.Current.GoToAsync("//" + route);
            }
            // "BDM.exe --find=<words>": open Smart Finder and search.
            if (StartupArgs.FirstOrDefault(a => a.StartsWith("--find=", StringComparison.Ordinal)) is { } findArg)
            {
                await Task.Delay(500);
                await Shell.Current.GoToAsync("//finder");
                if (Shell.Current.CurrentPage is Views.FinderPage finder) finder.Search(findArg["--find=".Length..].Trim('"'));
            }
            // "BDM.exe --add=<link>": open the Add page with that link (browser hand-off, testing).
            if (StartupArgs.FirstOrDefault(a => a.StartsWith("--add=", StringComparison.Ordinal)) is { } addArg)
            {
                await Task.Delay(500);
                await AppNavigation.OpenAddAsync(addArg["--add=".Length..].Trim('"'));
            }
            // "BDM.exe --download=<link>": start downloading right away (scripts, browser hand-off).
            if (StartupArgs.FirstOrDefault(a => a.StartsWith("--download=", StringComparison.Ordinal)) is { } dl)
            {
                var url = dl["--download=".Length..].Trim('"');
                if (UrlClassifier.ProtectedServiceName(url) is { } service)
                {
                    AppLog.Info("Refused DRM-protected link from command line: " + url);
                    await AppNavigation.OpenAddAsync(url);
                    return;
                }
                var kind = UrlClassifier.Classify(url) switch
                {
                    UrlKind.Media => DownloadKind.Media,
                    UrlKind.Hls => DownloadKind.Hls,
                    UrlKind.Torrent => DownloadKind.Torrent,
                    _ => DownloadKind.Http,
                };
                _manager.Add(new DownloadJob { Url = url, Kind = kind, MediaQuality = _manager.Settings.DefaultMediaQuality }, startNow: true);
                AppLog.Info($"Started from command line: {kind} {url}");
            }
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

    /// <summary>Command-line switches (desktop): --self-test, --page=downloads|grabber|settings|about|add, --add=&lt;url&gt;, --download=&lt;url&gt;, --find=&lt;words&gt;.</summary>
    public static string[] StartupArgs { get; set; } = [];

    /// <summary>Opens every page and the Add dialog; used by the build check to catch runtime UI errors.</summary>
    private static async Task<bool> RunSelfTestAsync()
    {
        bool ok = true;
        foreach (var (route, type) in new[] { ("downloads", typeof(Views.DownloadsPage)), ("grabber", typeof(Views.GrabberPage)),
                                              ("settings", typeof(Views.SettingsPage)), ("about", typeof(Views.AboutPage)) })
        {
            try
            {
                await Shell.Current.GoToAsync("//" + route);
                await Task.Delay(700);
                bool pass = Shell.Current.CurrentPage?.GetType() == type;
                ok &= pass;
                AppLog.Info($"self-test {(pass ? "OK" : "FAILED")}: {route}");
            }
            catch (Exception ex)
            {
                ok = false;
                AppLog.Error($"self-test FAILED: {route}", ex);
            }
        }
        try
        {
            await AppNavigation.OpenAddAsync("https://example.com/file.zip");
            await Task.Delay(1200);
            bool pass = Shell.Current.Navigation.ModalStack.Count == 1;
            ok &= pass;
            AppLog.Info($"self-test {(pass ? "OK" : "FAILED")}: add dialog");
            await Shell.Current.Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            ok = false;
            AppLog.Error("self-test FAILED: add dialog", ex);
        }
        AppLog.Info(ok ? "self-test PASSED" : "self-test FAILED");
        return ok;
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
