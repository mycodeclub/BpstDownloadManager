namespace Bdm.Core.Media;

/// <summary>A way to download from video sites: yt-dlp on desktop, YoutubeExplode on mobile.</summary>
public interface IMediaBackend
{
    string Name { get; }

    /// <summary>Whether this backend can be used right now (e.g. yt-dlp installed).</summary>
    bool IsAvailable { get; }

    /// <summary>Whether this backend handles the URL (yt-dlp: 1000+ sites; YoutubeExplode: YouTube only).</summary>
    bool CanHandle(string url);

    Task<MediaInfo> ProbeAsync(string url, CancellationToken ct);

    Task DownloadAsync(DownloadJob job, int connections, IReadOnlyList<RateLimiter> limiters, CancellationToken ct);
}
