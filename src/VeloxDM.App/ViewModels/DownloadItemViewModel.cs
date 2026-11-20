using System;
using VeloxDM.Core;

namespace VeloxDM.App.ViewModels;

/// <summary>Row in the download list. <see cref="Refresh"/> is called by a UI timer.</summary>
public sealed class DownloadItemViewModel : ObservableObject
{
    private long _lastBytes = -1;
    private double _speed;
    private string _fileName = "";
    private string _sizeText = "";
    private double _progress;
    private string _progressText = "";
    private string _speedText = "";
    private string _etaText = "";
    private string _statusText = "";
    private string _category = "Other";
    private int _connections;
    private bool _isIndeterminate;

    public DownloadItemViewModel(DownloadJob job)
    {
        Job = job;
        Refresh(TimeSpan.Zero);
    }

    public DownloadJob Job { get; }

    public string FileName { get => _fileName; private set => Set(ref _fileName, value); }
    public string SizeText { get => _sizeText; private set => Set(ref _sizeText, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }
    public string SpeedText { get => _speedText; private set => Set(ref _speedText, value); }
    public string EtaText { get => _etaText; private set => Set(ref _etaText, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string Category { get => _category; private set => Set(ref _category, value); }
    public int Connections { get => _connections; private set => Set(ref _connections, value); }
    public bool IsIndeterminate { get => _isIndeterminate; private set => Set(ref _isIndeterminate, value); }
    public double Speed => _speed;
    public string AddedText => Job.AddedUtc.ToLocalTime().ToString("g");
    public string Url => Job.Url;

    public void Refresh(TimeSpan elapsed)
    {
        var job = Job;
        long done = job.DownloadedBytes;
        long? total = job.TotalBytes;

        // Exponentially smoothed speed so the number does not jump around.
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
        Category = job.Kind == DownloadKind.Media && !System.IO.Path.HasExtension(job.FileName) ? "Video" : UrlClassifier.CategoryOf(job.FileName);
        SizeText = total is > 0 ? Format.Bytes(total.Value) : "";
        Connections = job.Kind == DownloadKind.Http ? job.ActiveConnections : 0;

        double pct = job.Status == DownloadStatus.Completed ? 100 : total is > 0 ? Math.Min(100, done * 100.0 / total.Value) : 0;
        Progress = pct;
        IsIndeterminate = job.IsRunning && total is not > 0;
        ProgressText = job.Status == DownloadStatus.Completed ? "100%" : total is > 0 ? $"{pct:0.0}%" : done > 0 ? Format.Bytes(done) : "";

        SpeedText = job.IsRunning ? Format.Speed(_speed) : "";
        EtaText = job.IsRunning && _speed > 1 && total is > 0 ? Format.Duration(TimeSpan.FromSeconds((total.Value - done) / _speed)) : "";

        StatusText = job.Status switch
        {
            DownloadStatus.Queued => "Queued",
            DownloadStatus.Connecting => job.Stage ?? "Connecting",
            DownloadStatus.Downloading => job.Stage ?? (Connections > 0 ? $"Downloading ({Connections} conn.)" : "Downloading"),
            DownloadStatus.Paused => "Paused",
            DownloadStatus.Completed => "Completed",
            DownloadStatus.Failed => "Failed: " + job.Error,
            _ => job.Status.ToString(),
        };
    }

    public bool MatchesCategory(string category) => category switch
    {
        "All" => true,
        "Active" => Job.IsRunning || Job.Status == DownloadStatus.Queued,
        "Unfinished" => Job.Status != DownloadStatus.Completed,
        "Completed" => Job.Status == DownloadStatus.Completed,
        _ => Category == category,
    };
}
