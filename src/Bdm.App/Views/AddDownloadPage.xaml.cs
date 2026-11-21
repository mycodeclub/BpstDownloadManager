using System.Collections.ObjectModel;
using Bdm.App.Services;
using Bdm.Core;
using Bdm.Core.Http;
using Bdm.Core.Media;
using Bdm.Core.Torrent;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bdm.App.Views;

public sealed partial class TorrentFileChoice : ObservableObject
{
    public required string Path { get; init; }
    public required long Length { get; init; }
    public string SizeText => Format.Bytes(Length);
    [ObservableProperty] private bool selected = true;
}

public partial class AddDownloadPage : ContentPage
{
    private const string BlobHelp =
        "A blob: link is a temporary handle inside one browser tab, so it can't be downloaded directly. " +
        "Open the page in the Video Grabber, press Play, and download the real stream it finds.";

    private readonly DownloadManager _manager;
    private readonly IPlatformServices _platform;
    private readonly ObservableCollection<TorrentFileChoice> _torrentFiles = new();
    private CancellationTokenSource? _analyzeCts;
    private DownloadKind _kind = DownloadKind.Http;
    private string? _torrentPath;       // local .torrent (picked, shared, or downloaded from a URL)
    private TorrentMetaInfo? _torrentMeta;
    private bool _nameEdited, _settingName;

    public AddDownloadPage(DownloadManager manager, IPlatformServices platform, string? url, string? torrentPath)
    {
        InitializeComponent();
        _manager = manager;
        _platform = platform;
        FolderEntry.Text = manager.Settings.DownloadFolder;
        FolderEntry.IsReadOnly = !platform.CanChooseFolder;
        FolderButton.IsVisible = platform.CanChooseFolder;
        QualityPicker.ItemsSource = MediaQuality.Options.ToList();
        QualityPicker.SelectedItem = MediaQuality.Options.FirstOrDefault(o => o.Id == manager.Settings.DefaultMediaQuality) ?? MediaQuality.Options[0];
        TorrentFilesList.ItemsSource = _torrentFiles;
        ToolbarItems.Add(new ToolbarItem("Close", null, async () => await CloseAsync()));

        if (torrentPath is not null)
        {
            _torrentPath = torrentPath;
            UrlEntry.Text = Path.GetFileName(torrentPath);
            UrlEntry.IsEnabled = false;
            _ = LoadTorrentFileAsync(torrentPath);
        }
        else if (!string.IsNullOrWhiteSpace(url))
        {
            UrlEntry.Text = url; // triggers analysis
        }
        else
        {
            ShowState("", "Paste a link: any file, a YouTube or video page, a stream (.m3u8) or a magnet link.", "", canStart: false);
        }
    }

    // ------------------------------------------------------------------ analysis

    private async void Url_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_torrentPath is not null && !UrlEntry.IsEnabled) return;
        _analyzeCts?.Cancel();
        var cts = _analyzeCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(350, cts.Token);
            await AnalyzeAsync(UrlEntry.Text?.Trim() ?? "", cts.Token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task AnalyzeAsync(string url, CancellationToken ct)
    {
        SignInPanel.IsVisible = GrabberPanel.IsVisible = ToolsPanel.IsVisible = TorrentGroup.IsVisible = false;
        _torrentMeta = null;
        switch (UrlClassifier.Classify(url))
        {
            case UrlKind.Invalid:
                SetMode(DownloadKind.Http);
                ShowState("", url.Length == 0 ? "Paste a link." : "This doesn't look like a link BDM can download.", "", canStart: false);
                return;
            case UrlKind.Blob:
                ShowState("blob", "Browser-internal blob: link", BlobHelp, canStart: false);
                ShowGrabberOffer($"Opens {OriginOfBlob(url) ?? "the site"}. Go to the video, press Play, then tap Download.");
                return;
            case UrlKind.Torrent:
                await AnalyzeTorrentAsync(url, ct);
                return;
            case UrlKind.Hls:
                SetMode(DownloadKind.Hls);
                SetName(FileNameHelper.Sanitize(Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath)));
                ShowState("stream", "Video stream (HLS)", "Downloaded with the built-in stream downloader, many parts at once.", canStart: true);
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
        ShowState("file", "Checking link…", "", canStart: true, busy: true);
        try
        {
            var probe = await HttpProbe.ProbeAsync(_manager.Http, url, null, ct);
            if (probe.LooksLikeWebPage)
            {
                await AnalyzeMediaAsync(url, ct);
                return;
            }
            SetName(probe.FileName);
            var size = probe.TotalBytes is > 0 ? Format.Bytes(probe.TotalBytes.Value) : "unknown size";
            var resume = probe.SupportsRanges
                ? $"Resumable · up to {_manager.Settings.Connections} connections"
                : "The server doesn't allow resume: single connection";
            ShowState("file", $"{probe.FileName} ({size})", $"{resume} · {probe.ContentType ?? "unknown type"}", canStart: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            ShowState("file", "Couldn't check the link (you can still try).", ex.Message, canStart: true);
        }
    }

    private async Task AnalyzeMediaAsync(string url, CancellationToken ct)
    {
        SetMode(DownloadKind.Media);
        var backend = _manager.MediaFor(url);
        if (!backend.IsAvailable || !backend.CanHandle(url))
        {
            if (_platform.SupportsVideoTools)
            {
                ToolsText.Text = "Most video sites need the free yt-dlp tool. Install it once in Settings → Video tools.";
                ToolsPanel.IsVisible = true;
            }
            ShowState("video", "Video page", _platform.SupportsVideoTools
                ? "yt-dlp is needed for this site." : "On this device the built-in downloader supports YouTube; use the Video Grabber for other sites.", canStart: false);
            ShowGrabberOffer("Or open the page in the Video Grabber, press Play, and download the stream it finds.");
            return;
        }

        if (backend == _manager.YtDlp && _manager.Tools.FfmpegPath is null)
        {
            ToolsText.Text = "FFmpeg isn't installed, so HD video (separate video and audio streams) can't be merged.";
            ToolsPanel.IsVisible = true;
        }

        ShowState("video", "Reading video information…", backend.Name, canStart: true, busy: true);
        try
        {
            var info = await backend.ProbeAsync(url, ct);
            SetName(info.Title);
            ShowState("video", info.Title, string.Join(" · ", new[] { info.Site, info.Duration, backend.Name }.Where(s => !string.IsNullOrWhiteSpace(s))), canStart: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (MediaSignInRequiredException ex)
        {
            SignInPanel.IsVisible = true;
            ShowState("video", "Sign-in required", ex.Message, canStart: true);
        }
        catch (Exception ex)
        {
            ShowState("video", "Couldn't read the video information (you can still try).", ex.Message, canStart: true);
            ShowGrabberOffer("Not supported? Open the page in the Video Grabber, press Play, and download the stream it finds.");
        }
    }

    private async Task AnalyzeTorrentAsync(string url, CancellationToken ct)
    {
        SetMode(DownloadKind.Torrent);
        if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            var name = System.Web.HttpUtility.ParseQueryString(url.Substring(url.IndexOf('?') + 1)).Get("dn");
            SetName(string.IsNullOrWhiteSpace(name) ? "Torrent" : name);
            ShowState("torrent", string.IsNullOrWhiteSpace(name) ? "Magnet link" : name,
                "The file list arrives from other peers after the download starts. You can choose files then by removing and re-adding, or download everything.", canStart: true);
            return;
        }

        // .torrent URL: fetch the small .torrent file first so the user can pick files.
        ShowState("torrent", "Fetching .torrent file…", "", canStart: false, busy: true);
        try
        {
            var bytes = await _manager.Http.GetByteArrayAsync(url, ct);
            var temp = Path.Combine(FileSystem.CacheDirectory, Guid.NewGuid().ToString("N") + ".torrent");
            await File.WriteAllBytesAsync(temp, bytes, ct);
            _torrentPath = temp;
            await LoadTorrentFileAsync(temp);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            ShowState("torrent", "Couldn't fetch the .torrent file.", ex.Message, canStart: false);
        }
    }

    private async Task LoadTorrentFileAsync(string path)
    {
        SetMode(DownloadKind.Torrent);
        try
        {
            _torrentMeta = await TorrentService.ReadAsync(path);
            SetName(_torrentMeta.Name);
            _torrentFiles.Clear();
            foreach (var f in _torrentMeta.Files.OrderBy(f => f.Path))
                _torrentFiles.Add(new TorrentFileChoice { Path = f.Path, Length = f.Length });
            TorrentGroup.IsVisible = _torrentFiles.Count > 1;
            UpdateTorrentSummary();
        }
        catch (Exception ex)
        {
            ShowState("torrent", "This .torrent file can't be read.", ex.Message, canStart: false);
        }
    }

    private void UpdateTorrentSummary()
    {
        if (_torrentMeta is null) return;
        var chosen = _torrentFiles.Where(f => f.Selected).ToList();
        long size = _torrentFiles.Count == 0 ? _torrentMeta.Size : chosen.Sum(f => f.Length);
        TorrentFilesTitle.Text = $"Files in this torrent ({chosen.Count} of {_torrentFiles.Count} selected)";
        ShowState("torrent", $"{_torrentMeta.Name} ({Format.Bytes(size)})",
            $"{_torrentMeta.Files.Count} file(s) · BitTorrent with DHT and peer exchange", canStart: _torrentFiles.Count == 0 || chosen.Count > 0);
    }

    private void TorrentCheck_Changed(object? sender, CheckedChangedEventArgs e) => UpdateTorrentSummary();

    private void TorrentAll_Clicked(object? sender, EventArgs e)
    {
        foreach (var f in _torrentFiles) f.Selected = true;
        UpdateTorrentSummary();
    }

    private void TorrentNone_Clicked(object? sender, EventArgs e)
    {
        foreach (var f in _torrentFiles) f.Selected = false;
        UpdateTorrentSummary();
    }

    // ------------------------------------------------------------------ UI state

    private void SetMode(DownloadKind kind)
    {
        _kind = kind;
        QualityGroup.IsVisible = kind is DownloadKind.Media or DownloadKind.Hls;
        NameTitle.Text = kind switch
        {
            DownloadKind.Media => "Title",
            DownloadKind.Hls => "Save as",
            DownloadKind.Torrent => "Torrent",
            _ => "File name",
        };
        NameEntry.IsReadOnly = kind is DownloadKind.Media or DownloadKind.Torrent;
    }

    private void SetName(string name)
    {
        if (_nameEdited && _kind is DownloadKind.Http or DownloadKind.Hls) return;
        _settingName = true;
        NameEntry.Text = name;
        _settingName = false;
    }

    private void Name_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_settingName) _nameEdited = true;
    }

    private void ShowState(string kind, string info, string detail, bool canStart, bool busy = false)
    {
        KindBadge.IsVisible = kind.Length > 0;
        KindBadge.BackgroundColor = kind switch
        {
            "torrent" => Color.FromArgb("#7C3AED"),
            "video" or "stream" => Color.FromArgb("#14B8A6"),
            "blob" => Color.FromArgb("#F59E0B"),
            _ => Color.FromArgb("#2563EB"),
        };
        KindLabel.Text = kind.ToUpperInvariant();
        InfoLabel.Text = info;
        DetailLabel.Text = detail;
        DetailLabel.IsVisible = detail.Length > 0;
        Busy.IsRunning = Busy.IsVisible = busy;
        StartButton.IsEnabled = LaterButton.IsEnabled = canStart;
    }

    private void ShowGrabberOffer(string text)
    {
        GrabberText.Text = text;
        GrabberPanel.IsVisible = true;
    }

    private static string? OriginOfBlob(string blobUrl)
    {
        var inner = blobUrl.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ? blobUrl[5..] : blobUrl;
        return Uri.TryCreate(inner, UriKind.Absolute, out var u) ? u.GetLeftPart(UriPartial.Authority) + "/" : null;
    }

    // ------------------------------------------------------------------ buttons

    private async void Paste_Clicked(object? sender, EventArgs e)
    {
        try { UrlEntry.Text = (await Clipboard.Default.GetTextAsync())?.Trim(); } catch (Exception) { }
    }

    private async void Folder_Clicked(object? sender, EventArgs e)
    {
        var folder = await _platform.PickFolderAsync(FolderEntry.Text);
        if (folder is not null) FolderEntry.Text = folder;
    }

    private async void SignIn_Clicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new SignInPage(_manager, () => Url_TextChanged(null, null!)));

    private async void Grabber_Clicked(object? sender, EventArgs e)
    {
        var url = UrlEntry.Text?.Trim();
        await CloseAsync();
        await AppNavigation.OpenGrabberAsync(url);
    }

    private async void Tools_Clicked(object? sender, EventArgs e)
    {
        await CloseAsync();
        await Shell.Current.GoToAsync("//settings");
    }

    private async void Start_Clicked(object? sender, EventArgs e) => await FinishAsync(startNow: true);
    private async void Later_Clicked(object? sender, EventArgs e) => await FinishAsync(startNow: false);
    private async void Cancel_Clicked(object? sender, EventArgs e) => await CloseAsync();

    private async Task FinishAsync(bool startNow)
    {
        var url = UrlEntry.Text?.Trim() ?? "";
        var folder = FolderEntry.Text?.Trim() ?? _manager.Settings.DownloadFolder;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Can't use this folder", ex.Message, "OK");
            return;
        }

        var quality = (QualityPicker.SelectedItem as MediaQualityOption)?.Id;
        var job = new DownloadJob { Url = url, Kind = _kind, Folder = folder };
        switch (_kind)
        {
            case DownloadKind.Torrent:
                if (_torrentPath is not null)
                {
                    job.TorrentFile = _manager.Torrents.Import(_torrentPath, job.Id);
                    if (job.Url.Length == 0 || !UrlEntry.IsEnabled) job.Url = "file:" + Path.GetFileName(_torrentPath);
                    job.TorrentSkippedFiles = _torrentFiles.Where(f => !f.Selected).Select(f => f.Path).ToList();
                }
                job.FileName = NameEntry.Text?.Trim() ?? "Torrent";
                break;
            case DownloadKind.Hls:
                job.OutputName = string.IsNullOrWhiteSpace(NameEntry.Text) ? "video" : NameEntry.Text.Trim();
                job.FileName = job.OutputName;
                job.MediaQuality = quality;
                break;
            case DownloadKind.Media:
                job.FileName = NameEntry.Text?.Trim() ?? "";
                job.MediaQuality = quality;
                break;
            default:
                if (UrlClassifier.Classify(url) is UrlKind.Invalid or UrlKind.Blob) return;
                job.FileName = NameEntry.Text?.Trim() ?? "";
                break;
        }

        _manager.Add(job, startNow);
        await CloseAsync();
    }

    private async Task CloseAsync()
    {
        _analyzeCts?.Cancel();
        if (Navigation.ModalStack.Count > 0) await Navigation.PopModalAsync();
    }
}
