using System.Text.Json.Serialization;

namespace VeloxDM.Core;

public enum DownloadStatus { Queued, Connecting, Downloading, Paused, Completed, Failed }

public enum DownloadKind
{
    /// <summary>Plain HTTP(S) file, downloaded by the multi-connection engine.</summary>
    Http,
    /// <summary>Video/audio page or stream (YouTube, HLS, DASH...), downloaded through yt-dlp.</summary>
    Media,
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
    public string FileName { get; set; } = "";
    public string Folder { get; set; } = "";
    public DownloadKind Kind { get; set; }

    /// <summary>Quality preset id for media downloads (see <see cref="Media.MediaQuality"/>).</summary>
    public string? MediaQuality { get; set; }

    public long? TotalBytes { get; set; }
    public bool SupportsRanges { get; set; }
    public List<Segment> Segments { get; set; } = new();
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    public string? Error { get; set; }

    /// <summary>The last failure was because the site needs a signed-in account.</summary>
    public bool NeedsSignIn { get; set; }

    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }

    /// <summary>Bytes reported by yt-dlp for media jobs.</summary>
    public long MediaDownloaded { get; set; }

    /// <summary>Human readable sub-stage for media jobs ("Fetching info", "Merging"...).</summary>
    [JsonIgnore] public string? Stage { get; set; }

    /// <summary>Guards <see cref="Segments"/> and segment bookkeeping.</summary>
    [JsonIgnore] public object Sync { get; } = new();

    [JsonIgnore] public string FullPath => Path.Combine(Folder, FileName);
    [JsonIgnore] public string TempPath => FullPath + ".vdmpart";

    [JsonIgnore]
    public long DownloadedBytes
    {
        get
        {
            if (Kind == DownloadKind.Media) return MediaDownloaded;
            lock (Sync)
            {
                long sum = 0;
                foreach (var s in Segments) sum += s.Downloaded;
                return sum;
            }
        }
    }

    [JsonIgnore]
    public int ActiveConnections
    {
        get { lock (Sync) return Segments.Count(s => s.Active); }
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
                Id = Id, Url = Url, Referrer = Referrer, FileName = FileName, Folder = Folder, Kind = Kind,
                MediaQuality = MediaQuality, TotalBytes = TotalBytes, SupportsRanges = SupportsRanges,
                Segments = Segments.Select(s => s.Clone()).ToList(), Status = Status, Error = Error, NeedsSignIn = NeedsSignIn,
                AddedUtc = AddedUtc, CompletedUtc = CompletedUtc, MediaDownloaded = MediaDownloaded,
            };
        }
    }
}
