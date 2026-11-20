using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using VeloxDM.Core;
using VeloxDM.Core.Http;
using VeloxDM.Core.Media;

namespace VeloxDM.App.Views;

public partial class AddDownloadWindow : Window
{
    private const string BlobHelp =
        "A blob: link is a temporary handle inside one browser tab, so it can't be downloaded directly. " +
        "Open the page in the Video Grabber, press Play, and download the real stream it detects. " +
        "(Or paste the page's own address here.)";
    private readonly DownloadManager _manager;
    private CancellationTokenSource? _analyzeCts;
    private DownloadKind _kind = DownloadKind.Http;
    private bool _nameEdited;
    private bool _settingName;

    public AddDownloadWindow(DownloadManager manager, string? url)
    {
        InitializeComponent();
        _manager = manager;
        FolderBox.Text = manager.Settings.DownloadFolder;
        QualityBox.ItemsSource = MediaQuality.Options;
        QualityBox.SelectedValue = manager.Settings.DefaultMediaQuality;
        NameBox.TextChanged += (_, _) => { if (!_settingName) _nameEdited = true; };
        SetUrl(url ?? "");
        Loaded += (_, _) => { UrlBox.Focus(); UrlBox.SelectAll(); };
    }

    public DownloadJob? Result { get; private set; }
    public bool StartNow { get; private set; }

    public void SetUrl(string url)
    {
        UrlBox.Text = url;
        if (url.Length == 0) ShowState("", "Paste or type a link to a file, a video page (YouTube etc.) or an HLS/DASH stream.", "", canStart: false);
    }

    private async void UrlBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _analyzeCts?.Cancel();
        var cts = _analyzeCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(350, cts.Token); // debounce typing
            await AnalyzeAsync(UrlBox.Text.Trim(), cts.Token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task AnalyzeAsync(string url, CancellationToken ct)
    {
        ToolsPanel.Visibility = Visibility.Collapsed;
        SignInPanel.Visibility = Visibility.Collapsed;
        GrabberPanel.Visibility = Visibility.Collapsed;
        switch (UrlClassifier.Classify(url))
        {
            case UrlKind.Invalid:
                ShowState("", url.Length == 0 ? "Paste or type a link." : "This is not an http(s) link.", "", canStart: false);
                SetMode(DownloadKind.Http);
                return;

            case UrlKind.Blob:
                ShowState("blob", "This is a browser-internal blob: link.", BlobHelp, canStart: false);
                ShowGrabberOffer($"Opens {GrabberWindow.OriginOfBlob(url) ?? "the site"}. Go to the video, press Play, then click Download.");
                return;

            case UrlKind.Media:
                await AnalyzeMediaAsync(url, ct);
                return;

            default:
                await AnalyzeHttpAsync(url, ct);
                return;
        }
    }

    private async Task AnalyzeHttpAsync(string url, CancellationToken ct)
    {
        SetMode(DownloadKind.Http);
        ShowState("file", "Checking link...", "", canStart: true);
        try
        {
            var probe = await HttpProbe.ProbeAsync(_manager.Http, url, null, ct);
            if (probe.LooksLikeWebPage)
            {
                // A web page: try the video extractor instead.
                await AnalyzeMediaAsync(url, ct);
                return;
            }
            SetName(probe.FileName);
            var size = probe.TotalBytes is > 0 ? Format.Bytes(probe.TotalBytes.Value) : "unknown size";
            var resume = probe.SupportsRanges
                ? $"Resumable · up to {_manager.Settings.Connections} connections"
                : "Server does not support resume: single connection";
            ShowState("file", $"{probe.FileName} ({size})", $"{resume} · {probe.ContentType ?? "unknown type"}", canStart: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            ShowState("file", "Could not check the link (you can still try).", ex.Message, canStart: true);
        }
    }

    private async Task AnalyzeMediaAsync(string url, CancellationToken ct)
    {
        SetMode(DownloadKind.Media);
        UpdateToolsWarning();
        if (_manager.Tools.YtDlpPath is null)
        {
            ShowState("video", "Video page or stream.", "yt-dlp is required for this link.", canStart: false);
            return;
        }

        ShowState("video", "Reading video information...", "", canStart: true);
        try
        {
            var info = await _manager.Media.ProbeAsync(url, ct);
            ct.ThrowIfCancellationRequested();
            SetName(info.Title);
            ShowState("video", info.Title, string.Join(" · ", new[] { info.Site, info.Duration }.NonEmpty()), canStart: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (MediaSignInRequiredException ex)
        {
            SignInPanel.Visibility = Visibility.Visible;
            ShowState("video", "Sign-in required.", ex.Message, canStart: true);
        }
        catch (Exception ex)
        {
            ShowState("video", "Could not read video information (you can still try).", ex.Message, canStart: true);
            ShowGrabberOffer("Not a supported site? Open the page in the Video Grabber, press Play, and download the stream it detects.");
        }
    }

    private void ShowGrabberOffer(string text)
    {
        GrabberText.Text = text;
        GrabberPanel.Visibility = Visibility.Visible;
    }

    private void Grabber_Click(object sender, RoutedEventArgs e)
    {
        GrabberWindow.Open(_manager, UrlBox.Text.Trim());
        DialogResult = false; // the grabber takes over from here
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (new YouTubeSignInWindow(_manager) { Owner = this }.ShowDialog() != true) return;
        _analyzeCts?.Cancel();
        var cts = _analyzeCts = new CancellationTokenSource();
        try { await AnalyzeAsync(UrlBox.Text.Trim(), cts.Token); } catch (OperationCanceledException) { }
    }

    private void UpdateToolsWarning()
    {
        var tools = _manager.Tools;
        string? msg = tools.YtDlpPath is null
            ? "Video sites and streams need yt-dlp (free, open source). Install it once with one click."
            : tools.FfmpegPath is null
                ? "FFmpeg is not installed: high-resolution video (separate video + audio streams) cannot be merged, so quality may be limited."
                : tools.JsRuntimePath is null
                    ? "Deno is not installed: YouTube may offer fewer formats without a JavaScript runtime."
                    : null;
        ToolsPanel.Visibility = msg is null ? Visibility.Collapsed : Visibility.Visible;
        ToolsText.Text = msg ?? "";
    }

    private void SetMode(DownloadKind kind)
    {
        _kind = kind;
        bool media = kind == DownloadKind.Media;
        QualityLabel.Visibility = QualityBox.Visibility = media ? Visibility.Visible : Visibility.Collapsed;
        NameLabel.Text = media ? "Title" : "File name";
        NameBox.IsReadOnly = media; // yt-dlp names the file from the title
    }

    private void SetName(string name)
    {
        if (_nameEdited && _kind == DownloadKind.Http) return;
        _settingName = true;
        NameBox.Text = name;
        _settingName = false;
    }

    private void ShowState(string kind, string info, string detail, bool canStart)
    {
        KindBadge.Visibility = kind.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        KindText.Text = kind.ToUpperInvariant();
        InfoText.Text = info;
        DetailText.Text = detail;
        DetailText.Visibility = detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        StartButton.IsEnabled = LaterButton.IsEnabled = canStart;
    }

    private async void InstallTools_Click(object sender, RoutedEventArgs e)
    {
        new ToolsWindow(_manager) { Owner = this }.ShowDialog();
        UpdateToolsWarning();
        _analyzeCts?.Cancel();
        var cts = _analyzeCts = new CancellationTokenSource();
        try { await AnalyzeAsync(UrlBox.Text.Trim(), cts.Token); } catch (OperationCanceledException) { }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : null };
        if (dlg.ShowDialog(this) == true) FolderBox.Text = dlg.FolderName;
    }

    private void Start_Click(object sender, RoutedEventArgs e) => Finish(startNow: true);
    private void Later_Click(object sender, RoutedEventArgs e) => Finish(startNow: false);

    private void Finish(bool startNow)
    {
        var url = UrlBox.Text.Trim();
        var kind = UrlClassifier.Classify(url);
        if (kind is UrlKind.Invalid or UrlKind.Blob) return;

        var folder = FolderBox.Text.Trim();
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Cannot use this folder:\n{ex.Message}", "VeloxDM");
            return;
        }

        if (_kind == DownloadKind.Media && _manager.Tools.YtDlpPath is null)
        {
            MessageBox.Show(this, "Install the video tools first (yt-dlp).", "VeloxDM");
            return;
        }

        Result = new DownloadJob
        {
            Url = url,
            Kind = _kind,
            Folder = folder,
            FileName = NameBox.Text.Trim(),
            MediaQuality = _kind == DownloadKind.Media ? QualityBox.SelectedValue as string : null,
        };
        StartNow = startNow;
        _analyzeCts?.Cancel();
        DialogResult = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _analyzeCts?.Cancel();
        base.OnClosed(e);
    }
}

internal static class EnumerableExtensions
{
    public static System.Collections.Generic.IEnumerable<string> NonEmpty(this System.Collections.Generic.IEnumerable<string?> items)
    {
        foreach (var i in items) if (!string.IsNullOrWhiteSpace(i)) yield return i;
    }
}

