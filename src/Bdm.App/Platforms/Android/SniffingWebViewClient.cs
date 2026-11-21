using Android.Webkit;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Bdm.App.Droid;

/// <summary>
/// Keeps MAUI's normal WebView behaviour and additionally reports every request URL (+ its headers)
/// so the Video Grabber can spot HLS/DASH playlists and video files behind "blob:" players.
/// </summary>
public sealed class SniffingWebViewClient : MauiWebViewClient
{
    private readonly Action<string, IDictionary<string, string>> _onRequest;

    public SniffingWebViewClient(WebViewHandler handler, Action<string, IDictionary<string, string>> onRequest) : base(handler)
    {
        _onRequest = onRequest;
    }

    public override WebResourceResponse? ShouldInterceptRequest(global::Android.Webkit.WebView? view, IWebResourceRequest? request)
    {
        try
        {
            if (request?.Url?.ToString() is { } url)
                _onRequest(url, request.RequestHeaders ?? new Dictionary<string, string>());
        }
        catch (Exception) { }
        return base.ShouldInterceptRequest(view, request);
    }
}
