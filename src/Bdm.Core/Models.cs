using System.Text.Json.Serialization;

namespace Bdm.Core;

public enum DownloadStatus { Queued, Connecting, Downloading, Paused, Completed, Failed, Seeding }

public enum DownloadKind
{
    /// <summary>Plain HTTP(S) file (programs, ISOs, zips, games...), multi-connection engine.</summary>
    Http,
    /// <summary>Video site page (YouTube etc.), DASH: yt-dlp on desktop, YoutubeExplode on mobile.</summary>
    Media,
    /// <summary>HLS stream (.m3u8), what most "blob:" players load. Built-in downloader, every platform.</summary>
    Hls,
    /// <summary>BitTorrent: magnet link or .torrent file.</summary>
    Torrent,
}

/// <summary>A byte range of the target file handled by one connection.</summary>
public sealed class Segment
{
    public const long UnknownEnd = long.MaxValue;

    public long Start { get; set; }

    /// <summary>Inclusive end offset, or <see cref="UnknownEnd"/> when the size is unknown.</summary>
    public long End { get; set; }

    public long Downloaded { get; set; }

    [JsonIgnore] public long Position => Start + Downloaded;
    [JsonIgnore] public long Remaining => End == UnknownEnd ? long.MaxValue : End - Position + 1;
    [JsonIgnore] public bool IsDone => End != UnknownEnd && Position > End;
    [JsonIgnore] public bool Active { get; internal set; }

    public Segment Clone() => new() { Start = Start, End = End, Downloaded = Downloaded };
}

public readonly record struct SegmentInfo(long Start, long End, long Downloaded, bool Active);

public sealed class DownloadJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = "";
    public string? Referrer { get; set; }

    /// <summary>Extra request headers captured from the page (Origin, User-Agent, Cookie...).</summary>
    public Dictionary<string, string>? Headers { get; set; }

    public string FileName { get; set; } = "";
    public string Folder { get; set; } = "";
    public DownloadKind Kind { get; set; }

    /// <summary>Quality preset id for video downloads (see <see cref="Media.MediaQuality"/>).</summary>
    public string? MediaQuality { get; set; }

    /// <summary>For media/stream jobs: file name (without extension) to use instead of the stream's own title.</summary>
    public string? OutputName { get; set; }

    public long? TotalBytes { get; set; }
    public bool SupportsRanges { get; set; }

    /// <summary>Server validators from the first request: used on resume to detect a changed file.</summary>
    public string? ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }

    public List<Segment> Segments { get; set; } = new();
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    public string? Error { get; set; }

    /// <summary>The last failure was because the site needs a signed-in account.</summary>
    public bool NeedsSignIn { get; set; }

    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }

    /// <summary>Progress reported by non-HTTP engines (video, HLS, torrent).</summary>
    public long MediaDownloaded { get; set; }

    // ---- HLS
    /// <summary>Indexes of HLS segments already saved (resume).</summary>
    public List<int>? HlsDone { get; set; }
    public int HlsSegmentCount { get; set; }

    // ---- Torrent
    /// <summary>Saved copy of the .torrent file (null for magnet links until metadata arrives).</summary>
    public string? TorrentFile { get; set; }
    /// <summary>Paths inside the torrent the user chose not to download.</summary>
    public List<string>? TorrentSkippedFiles { get; set; }
    public long TorrentUploaded { get; set; }

    /// <summary>
    /// Sub-downloads that are merged at the end (YouTube HD: a video-only and an audio-only stream).
    /// Each part keeps its own segments, so pausing and resuming works for both.
    /// </summary>
    public List<DownloadJob>? Parts { get; set; }

    /// <summary>Automatic retries used after network failures (long downloads).</summary>
    public int AutoRetries { get; set; }

    // ---- runtime only
    /// <summary>Human readable sub-stage ("Fetching info", "Merging", "12 peers"...).</summary>
    [JsonIgnore] public string? Stage { get; set; }

    /// <summary>Extra live detail for the UI (peers/seeds for torrents, segment count for HLS...).</summary>
    [JsonIgnore] public string? Detail { get; set; }

    /// <summary>Per-download speed limit (Focus mode slows the other downloads with it).</summary>
    [JsonIgnore] public RateLimiter Limiter { get; } = new();

    /// <summary>Paused automatically because another download has focus; resumes when focus ends.</summary>
    [JsonIgnore] public bool PausedByFocus { get; internal set; }

    /// <summary>Guards <see cref="Segments"/> and segment bookkeeping.</summary>
    [JsonIgnore] public object Sync { get; } = new();

    [JsonIgnore] public string FullPath => Path.Combine(Folder, FileName);
    [JsonIgnore] public string TempPath => FullPath + ".bdmpart";

    [JsonIgnore]
    public long DownloadedBytes
    {
        get
        {
            if (Parts is { Count: > 0 } parts) return parts.Sum(p => p.DownloadedBytes);
            lock (Sync)
            {
                // HTTP files and YouTube streams fetched by the segmented engine track progress per segment;
                // yt-dlp, HLS and torrent downloads report it in MediaDownloaded.
                if (Segments.Count == 0) return MediaDownloaded;
                long sum = 0;
                foreach (var s in Segments) sum += s.Downloaded;
                return sum;
            }
        }
    }

    [JsonIgnore]
    public int ActiveConnections
    {
        get
        {
            if (Parts is { Count: > 0 } parts) return parts.Sum(p => p.ActiveConnections);
            lock (Sync) return Segments.Count(s => s.Active);
        }
    }

    [JsonIgnore] internal CancellationTokenSource? Cts { get; set; }
    [JsonIgnore] internal Task? RunTask { get; set; }
    [JsonIgnore] public bool IsRunning => Cts is not null;

    public IReadOnlyList<SegmentInfo> SnapshotSegments()
    {
        lock (Sync) return Segments.Select(s => new SegmentInfo(s.Start, s.End, s.Downloaded, s.Active)).ToList();
    }

    /// <summary>Thread-safe copy used for persistence.</summary>
    public DownloadJob CloneForSave()
    {
        lock (Sync)
        {
            return new DownloadJob
            {
                Id = Id, Url = Url, Referrer = Referrer, Headers = Headers is null ? null : new(Headers), FileName = FileName,
                Folder = Folder, Kind = Kind, MediaQuality = MediaQuality, OutputName = OutputName, TotalBytes = TotalBytes,
                SupportsRanges = SupportsRanges, ETag = ETag, LastModified = LastModified,
                Segments = Segments.Select(s => s.Clone()).ToList(), Status = Status, Error = Error, NeedsSignIn = NeedsSignIn,
                AddedUtc = AddedUtc, CompletedUtc = CompletedUtc, MediaDownloaded = MediaDownloaded,
                HlsDone = HlsDone is null ? null : new(HlsDone), HlsSegmentCount = HlsSegmentCount,
                TorrentFile = TorrentFile, TorrentSkippedFiles = TorrentSkippedFiles is null ? null : new(TorrentSkippedFiles),
                TorrentUploaded = TorrentUploaded, AutoRetries = AutoRetries,
                Parts = Parts?.Select(p => p.CloneForSave()).ToList(),
            };
        }
    }
}

/// <summary>The server's copy of the file changed since the download started, so the parts don't fit together.</summary>
public sealed class RemoteFileChangedException(string message) : Exception(message);
