namespace Bdm.Core.Media;

public enum DetectedStreamKind
{
    /// <summary>HLS playlist (.m3u8): what most "blob:" players actually load.</summary>
    Hls,
    /// <summary>MPEG-DASH manifest (.mpd).</summary>
    Dash,
    /// <summary>A complete video/audio file (progressive MP4/WebM...).</summary>
    File,
}

public sealed record DetectedStream(DetectedStreamKind Kind, string Url, string? ContentType, long? Size)
{
    public string KindLabel => Kind switch
    {
        DetectedStreamKind.Hls => "HLS stream",
        DetectedStreamKind.Dash => "DASH stream",
        _ => "Video file",
    };
}

/// <summary>
/// Decides whether a network response seen by the grabber browser is a downloadable media stream.
/// Players that show a blob: URL feed the &lt;video&gt; element from one of these behind the scenes.
/// </summary>
public static class StreamDetector
{
    private const long MinFileSize = 512 * 1024; // skip thumbnails, previews, tiny ads

    private static readonly string[] SegmentExtensions = [".ts", ".m4s", ".aac", ".vtt", ".webvtt", ".key", ".m4f", ".cmfv", ".cmfa"];
    private static readonly string[] FileExtensions = [".mp4", ".webm", ".mkv", ".mov", ".m4v", ".flv", ".mp3", ".m4a", ".ogg", ".ogv"];

    /// <param name="totalSize">Full size of the resource (from Content-Range or Content-Length).</param>
    public static DetectedStream? Classify(string url, string? contentType, long? totalSize)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return null;
        var path = uri.AbsolutePath.ToLowerInvariant();
        var ct = contentType?.Split(';')[0].Trim().ToLowerInvariant() ?? "";

        if (path.EndsWith(".m3u8") || ct is "application/vnd.apple.mpegurl" or "application/x-mpegurl" or "audio/mpegurl" or "audio/x-mpegurl")
            return new DetectedStream(DetectedStreamKind.Hls, url, ct, null);

        if (path.EndsWith(".mpd") || ct == "application/dash+xml")
            return new DetectedStream(DetectedStreamKind.Dash, url, ct, null);

        // Individual stream segments are not useful on their own; the playlist above covers them.
        if (SegmentExtensions.Any(path.EndsWith) || ct is "video/mp2t" or "video/iso.segment" || path.Contains("/seg-") || path.Contains("segment"))
            return null;

        bool mediaType = ct.StartsWith("video/") || (ct.StartsWith("audio/") && ct != "audio/mpegurl");
        bool mediaExt = FileExtensions.Any(path.EndsWith);
        if ((mediaType || (mediaExt && ct is "" or "application/octet-stream" or "binary/octet-stream")) && totalSize is >= MinFileSize)
            return new DetectedStream(DetectedStreamKind.File, url, ct, totalSize);

        return null;
    }

    /// <summary>
    /// For browsers that only expose the request (Android WebView): decide from the address alone.
    /// Playlists are certain; video files are accepted by extension (thumbnails/segments filtered out).
    /// </summary>
    public static DetectedStream? ClassifyByUrl(string url)
    {
        var byType = Classify(url, null, null);
        if (byType is not null) return byType;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return null;
        var path = uri.AbsolutePath.ToLowerInvariant();
        if (SegmentExtensions.Any(path.EndsWith) || path.Contains("segment") || path.Contains("thumb") || path.Contains("preview")) return null;
        return path.EndsWith(".mp4") || path.EndsWith(".webm") || path.EndsWith(".mkv") || path.EndsWith(".m4v") || path.EndsWith(".mov")
            ? new DetectedStream(DetectedStreamKind.File, url, null, null)
            : null;
    }

    /// <summary>Parses the total from "bytes 0-1023/52428800".</summary>
    public static long? TotalFromContentRange(string? contentRange)
    {
        if (string.IsNullOrEmpty(contentRange)) return null;
        var slash = contentRange.LastIndexOf('/');
        return slash >= 0 && long.TryParse(contentRange[(slash + 1)..], out var total) ? total : null;
    }
}
