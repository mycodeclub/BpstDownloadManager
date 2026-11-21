using System.Collections.ObjectModel;
using Bdm.Core;
using Bdm.Core.Http;
using Bdm.Core.Media;

namespace Bdm.App.Views;

public sealed record DetectedItem(DetectedStream Stream, Dictionary<string, string> Headers, string PageUrl, string PageTitle)
{
    public string Type => Stream.KindLabel;
    public string Size => Stream.Size is long s ? Format.Bytes(s) : "";
    public string Url => Stream.Url;
}

/// <summary>
/// Built-in browser that watches the page's network traffic and lists the real media streams
/// (HLS .m3u8, DASH .mpd, MP4/WebM files) a player loads, which is how "blob:" videos are downloaded.
/// </summary>
public partial class GrabberPage : ContentPage
{
    // Records use of Encrypted Media Extensions (DRM) so we can explain why a video can't be saved.
    private const string DrmProbeScript =
        "(function(){if(window.__bdmHooked)return;window.__bdmHooked=true;var o=navigator.requestMediaKeySystemAccess;" +
        "if(o){navigator.requestMediaKeySystemAccess=function(){window.__bdmDrm=true;return o.apply(this,arguments);};}})();";

    private readonly DownloadManager _manager;
    private readonly ObservableCollection<DetectedItem> _streams = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly object _seenLock = new();
    private IDispatcherTimer? _drmTimer;
    private bool _hooked;

    public GrabberPage(DownloadManager manager)
    {
        InitializeComponent();
        _manager = manager;
        StreamList.ItemsSource = _streams;
        Web.HandlerChanged += (_, _) => HookPlatformWebView();
    }

    public void Navigate(string text)
    {
        text = text.Trim();
        if (text.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
        {
            StatusLabel.Text = "A blob: link can't be opened outside its tab, so the site was opened instead. Go to the video and press Play.";
            var inner = text[5..];
            text = Uri.TryCreate(inner, UriKind.Absolute, out var u) ? u.GetLeftPart(UriPartial.Authority) + "/" : "";
        }
        if (text.Length == 0) return;
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return;
        AddressEntry.Text = uri.ToString();
        Web.Source = new UrlWebViewSource { Url = uri.ToString() };
    }

    private void Go_Clicked(object? sender, EventArgs e) => Navigate(AddressEntry.Text ?? "");

    private void Back_Clicked(object? sender, EventArgs e)
    {
        if (Web.CanGoBack) Web.GoBack();
    }

    private void Clear_Clicked(object? sender, EventArgs e)
    {
        _streams.Clear();
        lock (_seenLock) _seen.Clear();
        DrmBanner.IsVisible = false;
        StatusLabel.Text = "";
    }

    private async void Web_Navigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Url is { } url) AddressEntry.Text = url;
        try { await Web.EvaluateJavaScriptAsync(DrmProbeScript); } catch (Exception) { }
        if (_drmTimer is null)
        {
            _drmTimer = Dispatcher.CreateTimer();
            _drmTimer.Interval = TimeSpan.FromSeconds(2);
            _drmTimer.Tick += async (_, _) =>
            {
                try
                {
                    var r = await Web.EvaluateJavaScriptAsync("window.__bdmDrm===true?'1':'0'");
                    if (r?.Trim('"') == "1") DrmBanner.IsVisible = true;
                }
                catch (Exception) { }
            };
            _drmTimer.Start();
        }
    }

    protected override void OnDisappearing()
    {
        _drmTimer?.Stop();
        _drmTimer = null;
        base.OnDisappearing();
    }

    /// <summary>Called from the platform hooks (any thread).</summary>
    private void OnStreamSeen(DetectedStream stream, Dictionary<string, string> headers)
    {
        lock (_seenLock)
            if (!_seen.Add(stream.Url)) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var pageUrl = AddressEntry.Text ?? "";
            var title = Title is "Video Grabber" or null ? "" : Title;
            _streams.Add(new DetectedItem(stream, headers, pageUrl, title));
            HintLabel.Text = $"{_streams.Count} stream(s) found. Download the first HLS item for all qualities.";
        });
    }

    private async void Download_Clicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not DetectedItem item) return;
        var s = item.Stream;
        string title = await PageTitleAsync();
        var headers = new Dictionary<string, string>(item.Headers, StringComparer.OrdinalIgnoreCase);
        var referrer = headers.Remove("Referer", out var r) ? r : item.PageUrl;

        DownloadJob job;
        switch (s.Kind)
        {
            case DetectedStreamKind.Hls:
                job = new DownloadJob
                {
                    Url = s.Url, Kind = DownloadKind.Hls, OutputName = title, FileName = title,
                    MediaQuality = _manager.Settings.DefaultMediaQuality, Referrer = referrer, Headers = headers,
                };
                break;
            case DetectedStreamKind.Dash:
                if (!_manager.YtDlp.IsAvailable)
                {
                    await DisplayAlertAsync("DASH stream", "DASH streams need the yt-dlp video tool (Windows: Settings → Video tools). Try an HLS or MP4 item if one is listed.", "OK");
                    return;
                }
                job = new DownloadJob { Url = s.Url, Kind = DownloadKind.Media, OutputName = title, FileName = title, Referrer = referrer, Headers = headers };
                break;
            default:
                var ext = Path.GetExtension(new Uri(s.Url).AbsolutePath);
                if (string.IsNullOrEmpty(ext) || ext.Length > 6) ext = s.ContentType?.Contains("webm") == true ? ".webm" : ".mp4";
                job = new DownloadJob { Url = s.Url, Kind = DownloadKind.Http, FileName = FileNameHelper.Sanitize(title) + ext, Referrer = referrer, Headers = headers };
                break;
        }
        _manager.Add(job, startNow: true);
        StatusLabel.Text = $"Added \"{job.FileName}\" to Downloads.";
    }

    private async Task<string> PageTitleAsync()
    {
        try
        {
            var t = (await Web.EvaluateJavaScriptAsync("document.title"))?.Trim('"');
            if (!string.IsNullOrWhiteSpace(t) && t != "null") return FileNameHelper.Sanitize(t);
        }
        catch (Exception) { }
        return "video";
    }

    // ------------------------------------------------------------------ platform hooks

    private void HookPlatformWebView()
    {
        if (_hooked || Web.Handler?.PlatformView is null) return;
        _hooked = true;
#if WINDOWS
        _ = HookWindowsAsync((Microsoft.UI.Xaml.Controls.WebView2)Web.Handler.PlatformView);
#elif ANDROID
        HookAndroid((Android.Webkit.WebView)Web.Handler.PlatformView);
#endif
    }

#if WINDOWS
    private async Task HookWindowsAsync(Microsoft.UI.Xaml.Controls.WebView2 wv)
    {
        await wv.EnsureCoreWebView2Async();
        var core = wv.CoreWebView2;
        core.NewWindowRequested += (_, e) => e.Handled = true; // block pop-up ads
        await core.AddScriptToExecuteOnDocumentCreatedAsync(DrmProbeScript);
        core.WebResourceResponseReceived += async (_, e) =>
        {
            try
            {
                var url = e.Request.Uri;
                var h = e.Response.Headers;
                string? type = h.Contains("Content-Type") ? h.GetHeader("Content-Type") : null;
                long? size = h.Contains("Content-Range") ? StreamDetector.TotalFromContentRange(h.GetHeader("Content-Range"))
                    : h.Contains("Content-Length") && long.TryParse(h.GetHeader("Content-Length"), out var len) ? len : null;
                var stream = StreamDetector.Classify(url, type, size);
                if (stream is null || e.Response.StatusCode >= 400) return;

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["User-Agent"] = core.Settings.UserAgent };
                if (e.Request.Headers.Contains("Origin")) headers["Origin"] = e.Request.Headers.GetHeader("Origin");
                if (e.Request.Headers.Contains("Referer")) headers["Referer"] = e.Request.Headers.GetHeader("Referer");
                var cookies = await core.CookieManager.GetCookiesAsync(url);
                if (cookies.Count > 0) headers["Cookie"] = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
                OnStreamSeen(stream, headers);
            }
            catch (Exception) { }
        };
    }
#endif

#if ANDROID
    private void HookAndroid(Android.Webkit.WebView wv)
    {
        wv.Settings.JavaScriptEnabled = true;
        wv.Settings.DomStorageEnabled = true;
        wv.Settings.SetSupportMultipleWindows(false); // no pop-up windows
        var userAgent = wv.Settings.UserAgentString ?? HttpFactory.UserAgent;
        wv.SetWebViewClient(new Bdm.App.Droid.SniffingWebViewClient((Microsoft.Maui.Handlers.WebViewHandler)Web.Handler!, (url, requestHeaders) =>
        {
            var stream = StreamDetector.ClassifyByUrl(url);
            if (stream is null) return;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["User-Agent"] = userAgent };
            foreach (var (k, v) in requestHeaders)
                if (k.Equals("Referer", StringComparison.OrdinalIgnoreCase) || k.Equals("Origin", StringComparison.OrdinalIgnoreCase)) headers[k] = v;
            var cookie = Android.Webkit.CookieManager.Instance?.GetCookie(url);
            if (!string.IsNullOrEmpty(cookie)) headers["Cookie"] = cookie;
            OnStreamSeen(stream, headers);
        }));
    }
#endif
}
