using Bdm.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bdm.App.ViewModels;

/// <summary>One row in the download list. <see cref="Refresh"/> is called by a UI timer.</summary>
public sealed partial class DownloadItemViewModel : ObservableObject
{
    private long _lastBytes = -1;
    private double _speed;

    public DownloadItemViewModel(DownloadJob job)
    {
        Job = job;
        Refresh(TimeSpan.Zero, null);
    }

    public DownloadJob Job { get; }

    [ObservableProperty] private string fileName = "";
    [ObservableProperty] private string kindGlyph = "";
    [ObservableProperty] private string sizeText = "";
    [ObservableProperty] private double progress;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private string speedText = "";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private string detailText = "";
    [ObservableProperty] private string category = "Other";
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool isCompleted;
    [ObservableProperty] private bool isFailed;
    [ObservableProperty] private bool isFocused;
    [ObservableProperty] private bool canFocus;
    [ObservableProperty] private string primaryActionText = "";

    public double Speed => _speed;

    public void Refresh(TimeSpan elapsed, Guid? focusedId)
    {
        var job = Job;
        long done = job.DownloadedBytes;
        long? total = job.TotalBytes;

        if (job.IsRunning && _lastBytes >= 0 && elapsed > TimeSpan.Zero)
        {
            double instant = Math.Max(0, done - _lastBytes) / elapsed.TotalSeconds;
            _speed = _speed <= 0 ? instant : _speed * 0.7 + instant * 0.3;
        }
        else if (!job.IsRunning)
        {
            _speed = 0;
        }
        _lastBytes = done;

        FileName = string.IsNullOrWhiteSpace(job.FileName) ? job.Url : job.FileName;
        KindGlyph = job.Kind switch
        {
            DownloadKind.Torrent => "🧲",
            DownloadKind.Media or DownloadKind.Hls => "🎬",
            _ => UrlClassifier.CategoryOf(job.FileName) switch
            {
                "Video" => "🎬", "Music" => "🎵", "Documents" => "📄", "Compressed" => "🗜", "Programs" => "💿", "Images" => "🖼", _ => "📦",
            },
        };
        Category = job.Kind == DownloadKind.Torrent ? "Torrents"
            : job.Kind is DownloadKind.Media or DownloadKind.Hls ? "Video"
            : UrlClassifier.CategoryOf(job.FileName);

        IsRunning = job.IsRunning;
        IsCompleted = job.Status == DownloadStatus.Completed;
        IsFailed = job.Status == DownloadStatus.Failed;
        IsFocused = focusedId == job.Id;
        CanFocus = !IsCompleted;

        double pct = IsCompleted ? 1 : total is > 0 ? Math.Min(1, done / (double)total.Value) : 0;
        Progress = pct;
        SizeText = total is > 0 ? (IsCompleted ? Format.Bytes(total.Value) : $"{Format.Bytes(done)} / {Format.Bytes(total.Value)}") : done > 0 ? Format.Bytes(done) : "";
        ProgressText = IsCompleted ? "100%" : total is > 0 ? $"{pct * 100:0.0}%" : "";
        SpeedText = job.IsRunning && _speed > 0 ? Format.Speed(_speed) : "";
        var eta = job.IsRunning && _speed > 1 && total is > 0 && done < total ? Format.Duration(TimeSpan.FromSeconds((total.Value - done) / _speed)) : "";

        StatusText = job.Status switch
        {
            DownloadStatus.Queued => "Queued",
            DownloadStatus.Connecting => job.Stage ?? "Connecting",
            DownloadStatus.Downloading => job.Stage ?? (eta.Length > 0 ? $"{eta} left" : "Downloading"),
            DownloadStatus.Seeding => job.Stage ?? "Seeding",
            DownloadStatus.Paused => job.PausedByFocus ? "Waiting (another download has focus)" : job.Error is { } e ? e : "Paused",
            DownloadStatus.Completed => "Completed",
            DownloadStatus.Failed => "Failed: " + job.Error,
            _ => job.Status.ToString(),
        };
        if (IsFocused && job.IsRunning) StatusText = "⚡ Focus · " + StatusText;

        DetailText = job.Kind == DownloadKind.Http && job.IsRunning
            ? $"{job.ActiveConnections} connections"
            : job.Detail ?? "";

        PrimaryActionText = IsCompleted ? "Open" : job.IsRunning ? "Pause" : IsFailed ? "Retry" : "Resume";
    }

    public bool Matches(string filter, string search)
    {
        if (search.Length > 0 && !FileName.Contains(search, StringComparison.OrdinalIgnoreCase) &&
            !Job.Url.Contains(search, StringComparison.OrdinalIgnoreCase)) return false;
        return filter switch
        {
            "All" => true,
            "Active" => Job.IsRunning || Job.Status == DownloadStatus.Queued,
            "Unfinished" => Job.Status != DownloadStatus.Completed,
            "Completed" => Job.Status == DownloadStatus.Completed,
            _ => Category == filter,
        };
    }
}
