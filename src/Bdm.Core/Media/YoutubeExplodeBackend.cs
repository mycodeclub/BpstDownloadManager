using Bdm.Core.Http;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace Bdm.Core.Media;

/// <summary>
/// YouTube without yt-dlp (Android/iOS, or Windows before the video tools are installed).
/// YouTube serves HD video and audio as separate streams; this backend downloads both with the
/// multi-connection engine (fast, resumable) and merges them with an <see cref="IStreamMuxer"/>
/// (Android MediaMuxer, or FFmpeg on desktop). Without a muxer it falls back to a combined stream if YouTube offers one.
/// </summary>
public sealed class YoutubeExplodeBackend : IMediaBackend
{
    private readonly HttpClient _http;
    private readonly Func<string?> _cookiesFile;
    private readonly Func<IStreamMuxer?> _muxer;

    public YoutubeExplodeBackend(HttpClient http, Func<string?> cookiesFile, Func<IStreamMuxer?> muxer)
    {
        _http = http;
        _cookiesFile = cookiesFile;
        _muxer = muxer;
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

    /// <summary>What will be downloaded for a quality preset.</summary>
    internal sealed record Plan(IStreamInfo? Video, IStreamInfo? Audio, IStreamInfo? Single, string Ext, string Label);

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

        var plan = Choose(manifest, job.MediaQuality, _muxer());
        job.Detail = plan.Label;
        var stem = FileNameHelper.Sanitize(string.IsNullOrWhiteSpace(job.OutputName) ? video.Title : job.OutputName);

        if (plan.Single is { } single)
        {
            await DownloadSingleAsync(job, single, stem + plan.Ext, connections, limiters, ct);
            return;
        }

        // Separate video + audio: download both (each resumable), then merge into one file.
        var parts = PrepareParts(job, plan, stem);
        job.TotalBytes = parts.Sum(p => p.TotalBytes ?? 0);
        job.FileName = string.IsNullOrWhiteSpace(job.FileName) || job.Status == DownloadStatus.Connecting
            ? FileNameHelper.GetUniqueFileName(job.Folder, stem + plan.Ext) : job.FileName;
        job.Status = DownloadStatus.Downloading;
        job.Stage = null;

        var streams = new[] { plan.Video!, plan.Audio! };
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part.Status == DownloadStatus.Completed && File.Exists(part.FullPath)) continue;
            job.Stage = i == 0 ? null : "Downloading audio";
            await Fetcher(streams[i], connections, limiters).DownloadAsync(part, ct);
            part.Status = DownloadStatus.Completed;
        }

        job.Stage = "Merging video and audio";
        var muxer = _muxer() ?? throw new InvalidOperationException("No way to merge video and audio on this device.");
        var output = Path.Combine(job.Folder, job.FileName);
        var temp = output + ".bdmpart";
        await muxer.MergeAsync(parts[0].FullPath, parts[1].FullPath, temp, ct);
        if (File.Exists(output)) job.FileName = FileNameHelper.GetUniqueFileName(job.Folder, job.FileName);
        File.Move(temp, job.FullPath);
        foreach (var p in parts) TryDelete(p.FullPath);
        job.Parts = null;
        job.TotalBytes = new FileInfo(job.FullPath).Length;
        job.MediaDownloaded = job.TotalBytes.Value;
        job.Stage = null;
    }

    /// <summary>Re-uses saved parts when the same streams were chosen before (resume), otherwise starts fresh.</summary>
    private static List<DownloadJob> PrepareParts(DownloadJob job, Plan plan, string stem)
    {
        var wanted = new[] { (Stream: plan.Video!, Tag: "video"), (Stream: plan.Audio!, Tag: "audio") };
        bool same = job.Parts is { Count: 2 } existing &&
                    existing[0].Url == Key(wanted[0].Stream) && existing[1].Url == Key(wanted[1].Stream) &&
                    existing[0].TotalBytes == wanted[0].Stream.Size.Bytes && existing[1].TotalBytes == wanted[1].Stream.Size.Bytes;
        if (!same)
        {
            if (job.Parts is not null) foreach (var p in job.Parts) { TryDelete(p.TempPath); TryDelete(p.FullPath); }
            lock (job.Sync) job.Segments.Clear();
            job.Parts = wanted.Select(w => new DownloadJob
            {
                Url = Key(w.Stream),
                Kind = DownloadKind.Http,
                Folder = job.Folder,
                FileName = $"{stem}.{job.Id.ToString("N")[..8]}.{w.Tag}.{w.Stream.Container.Name}.bdmtmp",
                TotalBytes = w.Stream.Size.Bytes,
                Referrer = "https://www.youtube.com/",
            }).ToList();
        }
        return job.Parts!;
    }

    // Identity of a stream across runs (its URL changes every time, the itag/size do not).
    private static string Key(IStreamInfo s) => $"youtube-stream:{s.Container.Name}:{s.Bitrate.BitsPerSecond}:{s.Size.Bytes}";

    private async Task DownloadSingleAsync(DownloadJob job, IStreamInfo stream, string fileName, int connections, IReadOnlyList<RateLimiter> limiters, CancellationToken ct)
    {
        job.Parts = null;
        bool resumable = job.Segments.Count > 0 && job.TotalBytes == stream.Size.Bytes && File.Exists(job.TempPath);
        if (!resumable)
        {
            lock (job.Sync) job.Segments.Clear();
            job.TotalBytes = null;
            job.FileName = FileNameHelper.GetUniqueFileName(job.Folder, fileName);
        }
        job.Stage = null;
        await Fetcher(stream, connections, limiters).DownloadAsync(job, ct);
    }

    /// <summary>
    /// YouTube's video servers throttle ordinary Range requests; requesting ~10 MB pieces with the
    /// "&amp;range=" URL parameter over several connections downloads at full line speed.
    /// </summary>
    private SegmentedDownloader Fetcher(IStreamInfo stream, int connections, IReadOnlyList<RateLimiter> limiters) => new(_http)
    {
        Connections = Math.Clamp(connections, 1, 16),
        Limiters = limiters,
        UrlOverride = stream.Url,
        KnownSize = stream.Size.Bytes,
        RangeInQuery = true,
        MaxRequestBytes = 10L * 1024 * 1024,
        MinSegmentSize = 1024 * 1024,
    };

    /// <summary>Chooses streams for a quality preset ("best", "mp4", "1080", "audio-m4a"...).</summary>
    internal static Plan Choose(StreamManifest manifest, string? quality, IStreamMuxer? muxer)
    {
        quality ??= MediaQuality.Best;
        var audios = manifest.GetAudioOnlyStreams().ToList();
        var bestAudio = audios
            .OrderByDescending(a => a.Container == Container.Mp4)   // AAC/.m4a: plays everywhere, works with Android's muxer
            .ThenByDescending(a => a.Bitrate)
            .FirstOrDefault();

        if (quality.StartsWith("audio", StringComparison.Ordinal))
        {
            if (bestAudio is null) throw new InvalidOperationException("YouTube offers no audio stream for this video.");
            return new Plan(null, null, bestAudio, bestAudio.Container == Container.Mp4 ? ".m4a" : ".webm", $"Audio {bestAudio.Bitrate}");
        }

        int maxHeight = int.TryParse(quality, out var h) ? h : int.MaxValue;
        if (muxer is { IsAvailable: true } && bestAudio is not null)
        {
            var candidates = manifest.GetVideoOnlyStreams()
                .Where(v => v.VideoQuality.MaxHeight <= maxHeight)
                .Where(v => muxer.Supports(v.VideoCodec, bestAudio.AudioCodec))
                .ToList();
            // MP4 output: prefer MP4-contained video (H.264/AV1) so the result plays everywhere; then highest quality.
            var video = candidates
                .OrderByDescending(v => v.VideoQuality.MaxHeight)
                .ThenByDescending(v => v.Container == Container.Mp4)
                .ThenByDescending(v => v.VideoQuality.Framerate)
                .ThenByDescending(v => v.Bitrate)
                .FirstOrDefault();
            if (video is not null)
            {
                bool mp4 = video.Container == Container.Mp4 && bestAudio.Container == Container.Mp4;
                return new Plan(video, bestAudio, null, mp4 ? ".mp4" : ".mkv", $"{video.VideoQuality.Label} · {video.VideoCodec.Split('.')[0]} + audio");
            }
        }

        // No way to merge: a combined stream, if YouTube still offers one for this video.
        var muxed = manifest.GetMuxedStreams().ToList();
        var single = muxed.Where(m => m.VideoQuality.MaxHeight <= maxHeight).OrderByDescending(m => m.VideoQuality.MaxHeight).FirstOrDefault()
                     ?? muxed.OrderBy(m => m.VideoQuality.MaxHeight).FirstOrDefault();
        if (single is null)
            throw new InvalidOperationException("YouTube serves this video as separate video and audio streams, and this device can't merge them. On Windows, install the video tools in Settings.");
        return new Plan(null, null, single, "." + single.Container.Name, $"{single.VideoQuality.Label} (combined stream)");
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

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
