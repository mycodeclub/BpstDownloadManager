using System.Net;
using Bdm.Core.Http;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace Bdm.Core.Media;

/// <summary>
/// YouTube without external tools (Android/iOS, or Windows before yt-dlp is installed). Finds the video's
/// stream with YoutubeExplode, then downloads that stream with the multi-connection engine, so it is fast
/// and resumable. Without FFmpeg, separate HD video and audio can't be merged, so video uses the best
/// combined (muxed) stream; audio-only presets use the best audio stream.
/// </summary>
public sealed class YoutubeExplodeBackend : IMediaBackend
{
    private readonly HttpClient _http;
    private readonly Func<string?> _cookiesFile;

    public YoutubeExplodeBackend(HttpClient http, Func<string?> cookiesFile)
    {
        _http = http;
        _cookiesFile = cookiesFile;
    }

    public string Name => "Built-in YouTube downloader";
    public bool IsAvailable => true;
    public bool CanHandle(string url) => VideoId.TryParse(url) is not null;

    private YoutubeClient CreateClient()
    {
        var cookies = _cookiesFile() is { } f && File.Exists(f) ? CookieFile.Read(f) : [];
        return new YoutubeClient(_http, cookies);
    }

    public async Task<MediaInfo> ProbeAsync(string url, CancellationToken ct)
    {
        try
        {
            var video = await CreateClient().Videos.GetAsync(url, ct);
            return new MediaInfo(video.Title, video.Duration?.ToString(), "YouTube");
        }
        catch (Exception ex) when (ex is YoutubeExplode.Exceptions.YoutubeExplodeException)
        {
            throw Translate(ex);
        }
    }

    public async Task DownloadAsync(DownloadJob job, int connections, IReadOnlyList<RateLimiter> limiters, CancellationToken ct)
    {
        Directory.CreateDirectory(job.Folder);
        job.Status = DownloadStatus.Connecting;
        job.Stage = "Reading video info";

        var client = CreateClient();
        Video video;
        StreamManifest manifest;
        try
        {
            video = await client.Videos.GetAsync(job.Url, ct);
            manifest = await client.Videos.Streams.GetManifestAsync(video.Id, ct);
        }
        catch (Exception ex) when (ex is YoutubeExplode.Exceptions.YoutubeExplodeException)
        {
            throw Translate(ex);
        }

        var (stream, ext, label) = Pick(manifest, job.MediaQuality);
        job.Detail = label;

        // Same stream size as before = the saved parts still fit, so resume; otherwise start over.
        bool resumable = job.Segments.Count > 0 && job.TotalBytes == stream.Size.Bytes && File.Exists(job.TempPath);
        if (!resumable)
        {
            lock (job.Sync) job.Segments.Clear();
            job.TotalBytes = null;
            var stem = FileNameHelper.Sanitize(string.IsNullOrWhiteSpace(job.OutputName) ? video.Title : job.OutputName);
            job.FileName = FileNameHelper.GetUniqueFileName(job.Folder, stem + ext);
        }

        job.Stage = null;
        var dl = new SegmentedDownloader(_http)
        {
            Connections = Math.Clamp(connections, 1, 16),
            Limiters = limiters,
            UrlOverride = stream.Url, // stream addresses expire, so a fresh one is used every run
        };
        await dl.DownloadAsync(job, ct);
    }

    private static (IStreamInfo Stream, string Ext, string Label) Pick(StreamManifest manifest, string? quality)
    {
        quality ??= MediaQuality.Best;
        if (quality.StartsWith("audio", StringComparison.Ordinal))
        {
            var audio = manifest.GetAudioOnlyStreams()
                .OrderByDescending(a => a.Container == Container.Mp4) // .m4a plays everywhere
                .ThenByDescending(a => a.Bitrate)
                .FirstOrDefault() ?? throw new InvalidOperationException("No audio stream found for this video.");
            return (audio, audio.Container == Container.Mp4 ? ".m4a" : ".webm", $"Audio {audio.Bitrate}");
        }

        int? maxHeight = int.TryParse(quality, out var h) ? h : null;
        var muxed = manifest.GetMuxedStreams().ToList();
        var pick = muxed
            .Where(m => maxHeight is null || m.VideoQuality.MaxHeight <= maxHeight)
            .OrderByDescending(m => m.VideoQuality.MaxHeight)
            .ThenByDescending(m => quality == "mp4" ? m.Container == Container.Mp4 : false)
            .FirstOrDefault() ?? muxed.OrderBy(m => m.VideoQuality.MaxHeight).FirstOrDefault();
        if (pick is null)
            throw new InvalidOperationException("YouTube offers no combined video+audio stream for this video. Install the video tools (Windows) for HD downloads.");
        return (pick, "." + pick.Container.Name, $"{pick.VideoQuality.Label} (built-in)");
    }

    private static Exception Translate(Exception ex)
    {
        var msg = ex.Message;
        if (msg.Contains("age", StringComparison.OrdinalIgnoreCase) || msg.Contains("sign in", StringComparison.OrdinalIgnoreCase))
            return new MediaSignInRequiredException("This video needs a signed-in YouTube account (age-restricted or private). Use Sign in to YouTube in Settings, then resume.");
        if (ex is YoutubeExplode.Exceptions.VideoUnavailableException)
            return new InvalidOperationException("This video is unavailable (removed, private, or blocked in your country).");
        return new InvalidOperationException(msg);
    }
}
