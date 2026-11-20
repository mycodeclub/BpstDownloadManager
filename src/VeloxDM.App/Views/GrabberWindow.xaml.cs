using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using VeloxDM.Core;
using VeloxDM.Core.Http;
using VeloxDM.Core.Media;

namespace VeloxDM.App.Views;

/// <summary>
/// Built-in browser that watches the page's network traffic and lists the real media streams
/// (HLS .m3u8, DASH .mpd, MP4/WebM files) that a player is loading. This is how videos shown
/// as "blob:" URLs are downloaded: the blob only exists in the tab, the stream behind it is a normal URL.
/// </summary>
public partial class GrabberWindow : Window
{
    private static GrabberWindow? _instance;

    // Reports DRM (Encrypted Media Extensions) use so the user knows why a protected video can't be saved.
    private const string DrmProbeScript = """
        (() => {
          const orig = navigator.requestMediaKeySystemAccess;
          if (!orig) return;
          navigator.requestMediaKeySystemAccess = function (keySystem) {
            try { window.chrome.webview.postMessage(JSON.stringify({ type: 'drm', keySystem })); } catch (e) {}
            return orig.apply(this, arguments);
          };
        })();
        """;

    private readonly DownloadManager _manager;
    private readonly ObservableCollection<StreamItem> _streams = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private string? _pendingUrl;

    private GrabberWindow(DownloadManager manager, string? url)
    {
        InitializeComponent();
        _manager = manager;
        _pendingUrl = url;
        StreamList.ItemsSource = _streams;
        AddressBox.Text = url ?? "";
        Loaded += async (_, _) => await InitAsync();
    }

    /// <summary>Opens the grabber (one window) and navigates to <paramref name="url"/>.</summary>
    public static void Open(DownloadManager manager, string? url)
    {
        if (_instance is { } w)
        {
            if (!string.IsNullOrWhiteSpace(url)) w.Navigate(url);
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            return;
        }
        _instance = new GrabberWindow(manager, url);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
    }

    /// <summary>"blob:https://site.com/uuid" → "https://site.com/" (the blob itself can't be opened).</summary>
    public static string? OriginOfBlob(string blobUrl)
    {
        var inner = blobUrl.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ? blobUrl[5..] : blobUrl;
        return Uri.TryCreate(inner, UriKind.Absolute, out var u) ? u.GetLeftPart(UriPartial.Authority) + "/" : null;
    }

    private async Task InitAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(_manager.DataDir, "webview"));
            await Web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            HintText.Text = "Could not start the built-in browser (Microsoft Edge WebView2 Runtime): " + ex.Message;
            return;
        }

        var core = Web.CoreWebView2;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(DrmProbeScript);
        core.WebResourceResponseReceived += OnResponse;
        core.WebMessageReceived += OnWebMessage;
        core.NewWindowRequested += (_, e) => e.Handled = true; // swallow pop-up ads
        core.SourceChanged += (_, _) => AddressBox.Text = core.Source;
        core.DocumentTitleChanged += (_, _) => Title = $"{core.DocumentTitle} - Video Grabber";

        if (!string.IsNullOrWhiteSpace(_pendingUrl)) Navigate(_pendingUrl);
    }

    private void Navigate(string text)
    {
        text = text.Trim();
        if (text.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "A blob: link can't be opened outside its tab. Opened the site instead: go to the video page and press Play.";
            text = OriginOfBlob(text) ?? "";
        }
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        if (Web.CoreWebView2 is null) { _pendingUrl = text; return; }
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
            Web.CoreWebView2.Navigate(uri.ToString());
    }

    private async void OnResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            var url = e.Request.Uri;
            if (_seen.Contains(url)) return;

            var h = e.Response.Headers;
            string? contentType = h.Contains("Content-Type") ? h.GetHeader("Content-Type") : null;
            long? size = h.Contains("Content-Range") ? StreamDetector.TotalFromContentRange(h.GetHeader("Content-Range"))
                       : h.Contains("Content-Length") && long.TryParse(h.GetHeader("Content-Length"), out var len) ? len : null;

            var stream = StreamDetector.Classify(url, contentType, size);
            if (stream is null || e.Response.StatusCode >= 400) return;
            _seen.Add(url);

            var headers = await CaptureHeadersAsync(e.Request, url);
            _streams.Add(new StreamItem(stream, headers, Web.CoreWebView2.Source, Web.CoreWebView2.DocumentTitle));
            HintText.Text = $"{_streams.Count} video stream(s) found. Click Download (the first HLS/DASH item usually holds all qualities).";
        }
        catch (Exception)
        {
            // A response vanished while we inspected it (navigation, aborted request): ignore.
        }
    }

    /// <summary>Headers a CDN may check: Referer, Origin, the browser's User-Agent and the site's cookies.</summary>
    private async Task<Dictionary<string, string>> CaptureHeadersAsync(CoreWebView2WebResourceRequest request, string url)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["User-Agent"] = Web.CoreWebView2.Settings.UserAgent,
        };
        if (request.Headers.Contains("Origin")) headers["Origin"] = request.Headers.GetHeader("Origin");
        if (request.Headers.Contains("Referer")) headers["Referer"] = request.Headers.GetHeader("Referer");

        var cookies = await Web.CoreWebView2.CookieManager.GetCookiesAsync(url);
        if (cookies.Count > 0) headers["Cookie"] = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
        return headers;
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            if (doc.RootElement.GetProperty("type").GetString() == "drm") DrmBanner.Visibility = Visibility.Visible;
        }
        catch (Exception) { }
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not StreamItem item) return;
        var s = item.Stream;
        var title = string.IsNullOrWhiteSpace(item.PageTitle) ? "video" : item.PageTitle;
        var headers = new Dictionary<string, string>(item.Headers, StringComparer.OrdinalIgnoreCase);
        var referrer = headers.Remove("Referer", out var r) ? r : item.PageUrl;

        DownloadJob job;
        if (s.Kind is DetectedStreamKind.Hls or DetectedStreamKind.Dash)
        {
            job = new DownloadJob
            {
                Url = s.Url, Kind = DownloadKind.Media, OutputName = title, FileName = title,
                MediaQuality = _manager.Settings.DefaultMediaQuality, Referrer = referrer, Headers = headers,
            };
        }
        else
        {
            var ext = Path.GetExtension(new Uri(s.Url).AbsolutePath);
            if (string.IsNullOrEmpty(ext) || ext.Length > 6) ext = s.ContentType?.Contains("webm") == true ? ".webm" : ".mp4";
            job = new DownloadJob
            {
                Url = s.Url, Kind = DownloadKind.Http, FileName = FileNameHelper.Sanitize(title) + ext,
                Referrer = referrer, Headers = headers,
            };
        }

        if (job.Kind == DownloadKind.Media && _manager.Tools.YtDlpPath is null)
        {
            MessageBox.Show(this, "Streams need the video tools. Open VeloxDM → Video tools and install yt-dlp and FFmpeg first.", "VeloxDM");
            return;
        }

        _manager.Add(job, startNow: true);
        StatusText.Text = $"Added \"{job.FileName}\" to downloads.";
    }

    private void Go_Click(object sender, RoutedEventArgs e) => Navigate(AddressBox.Text);

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Navigate(AddressBox.Text);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2?.CanGoBack == true) Web.CoreWebView2.GoBack();
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => Web.CoreWebView2?.Reload();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _streams.Clear();
        _seen.Clear();
        DrmBanner.Visibility = Visibility.Collapsed;
    }

    protected override void OnClosed(EventArgs e)
    {
        Web.Dispose();
        base.OnClosed(e);
    }

    public sealed record StreamItem(DetectedStream Stream, IReadOnlyDictionary<string, string> Headers, string PageUrl, string PageTitle)
    {
        public string Type => Stream.KindLabel;
        public string Size => Stream.Size is long s ? Format.Bytes(s) : "";
        public string Url => Stream.Url;
    }
}
