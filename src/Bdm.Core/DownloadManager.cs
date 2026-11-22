using System.Text.Json;
using Bdm.Core.Hls;
using Bdm.Core.Http;
using Bdm.Core.Media;
using Bdm.Core.Torrent;

namespace Bdm.Core;

/// <summary>
/// Owns the download list, the queue (max concurrent downloads), bandwidth (global limit and Focus mode),
/// automatic retries for long downloads, and persistence. Platform-neutral: used by Windows, Android and iOS.
/// </summary>
public sealed class DownloadManager : IDisposable
{
    private readonly List<DownloadJob> _jobs = new();
    private readonly object _lock = new();
    private readonly string _jobsFile;
    private readonly string _settingsFile;
    private readonly Timer _autosave;
    private bool _wasBusy;

    public DownloadManager(string dataDir, string? defaultDownloadFolder = null)
    {
        DataDir = dataDir;
        Directory.CreateDirectory(dataDir);
        _jobsFile = Path.Combine(dataDir, "downloads.json");
        _settingsFile = Path.Combine(dataDir, "settings.json");
        Settings = AppSettings.Load(_settingsFile);
        if (string.IsNullOrWhiteSpace(Settings.DownloadFolder))
            Settings.DownloadFolder = defaultDownloadFolder ?? DefaultDownloadFolder();
        if (Settings.CookiesFile is { } cf && !File.Exists(cf) &&
            Path.GetFileName(cf).Equals("youtube-cookies.txt", StringComparison.OrdinalIgnoreCase) && File.Exists(SignInCookiesPath))
        {
            Settings.CookiesFile = SignInCookiesPath; // data folder was moved by a rename
        }

        Http = HttpFactory.CreateClient();
        Tools = new MediaTools(Path.Combine(dataDir, "tools"));
        YtDlp = new MediaDownloader(Tools);
        Ffmpeg = new FfmpegMuxer(() => Tools.FfmpegPath);
        YouTube = new YoutubeExplodeBackend(Http, () => Settings.CookiesFile,
            () => PlatformMuxer is { IsAvailable: true } pm ? pm : Ffmpeg.IsAvailable ? Ffmpeg : null);
        Torrents = new TorrentService(dataDir, Settings);
        GlobalLimiter.BytesPerSecond = Settings.GlobalSpeedLimitKBps * 1024L;
        ApplyMediaSettings();
        LoadJobs();
        _autosave = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    // ---------------------------------------------------------------- data folder

    public static string DefaultDataDir => LazyDataDir.Value;

    private static readonly Lazy<string> LazyDataDir = new(() =>
    {
        // Optional override, e.g. a portable install on a USB stick or a separate demo profile.
        if (Environment.GetEnvironmentVariable("BDM_DATA_DIR") is { Length: > 0 } custom) return custom;
        if (Environment.GetEnvironmentVariable("BPSTVD_DATA_DIR") is { Length: > 0 } old) return old;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(local, Branding.DataFolderName);
        bool newHasData = File.Exists(Path.Combine(dir, "settings.json")) || File.Exists(Path.Combine(dir, "downloads.json"));
        if (!newHasData)
        {
            // Earlier names of this app: carry the downloads list, settings, tools and sign-in over once.
            foreach (var legacyName in new[] { "BpstVideoDownloader", "VeloxDM" })
            {
                var legacy = Path.Combine(local, legacyName);
                if (Directory.Exists(legacy)) { MigrateLegacyData(legacy, dir); break; }
            }
        }
        return dir;
    });

    public static string DefaultDownloadFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    /// <summary>
    /// Copies (not moves: the old folder may be held open by Explorer, indexing or antivirus) the old
    /// data into the new folder, then removes the old folder if Windows allows it.
    /// </summary>
    internal static void MigrateLegacyData(string legacy, string dir)
    {
        bool complete = true;
        foreach (var src in Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories))
        {
            var dst = Path.Combine(dir, Path.GetRelativePath(legacy, src));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                complete = false; // e.g. a locked browser cache file: not needed
            }
        }

        var settingsPath = Path.Combine(dir, "settings.json");
        if (File.Exists(settingsPath))
        {
            var text = File.ReadAllText(settingsPath);
            var updated = text.Replace(JsonEscaped(legacy + Path.DirectorySeparatorChar), JsonEscaped(dir + Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            if (updated != text) File.WriteAllText(settingsPath, updated);
        }

        if (complete)
        {
            try { Directory.Delete(legacy, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        static string JsonEscaped(string s) => s.Replace("\\", "\\\\");
    }

    // ---------------------------------------------------------------- services

    public string DataDir { get; }
    public AppSettings Settings { get; }
    public HttpClient Http { get; }
    public MediaTools Tools { get; }
    public MediaDownloader YtDlp { get; }
    public YoutubeExplodeBackend YouTube { get; }
    public TorrentService Torrents { get; }
    public FfmpegMuxer Ffmpeg { get; }

    /// <summary>Set by the app on platforms with a built-in muxer (Android MediaMuxer).</summary>
    public IStreamMuxer? PlatformMuxer { get; set; }

    private readonly SemaphoreSlim _toolsGate = new(1, 1);

    /// <summary>
    /// Desktop: the first video download installs yt-dlp + FFmpeg (and Deno if no Node.js) automatically,
    /// so YouTube works out of the box. Returns false if that isn't possible (then the built-in downloader is used).
    /// </summary>
    private async Task<bool> EnsureVideoToolsAsync(DownloadJob job, CancellationToken ct)
    {
        if (!Tools.IsSupported || !Settings.AutoInstallVideoTools) return YtDlp.IsAvailable;
        if (YtDlp.IsAvailable && Tools.FfmpegPath is not null) return true;
        await _toolsGate.WaitAsync(ct);
        try
        {
            var progress = new Progress<string>(s => job.Stage = "One-time setup: " + s);
            if (Tools.YtDlpPath is null) await Tools.InstallYtDlpAsync(Http, progress, ct);
            if (Tools.FfmpegPath is null) await Tools.InstallFfmpegAsync(Http, progress, ct);
            if (Tools.JsRuntimePath is null) await Tools.InstallDenoAsync(Http, progress, ct);
            job.Stage = null;
            return YtDlp.IsAvailable;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            job.Stage = null;
            job.Detail = "Video tools couldn't be installed (" + ex.Message + "); using the built-in downloader.";
            return YtDlp.IsAvailable;
        }
        finally
        {
            _toolsGate.Release();
        }
    }

    /// <summary>Overall speed limit shared by all downloads.</summary>
    public RateLimiter GlobalLimiter { get; } = new();

    /// <summary>yt-dlp when installed (desktop, 1000+ sites), otherwise the built-in YouTube downloader.</summary>
    public IMediaBackend MediaFor(string url) =>
        YtDlp.IsAvailable ? YtDlp : YouTube.CanHandle(url) ? YouTube : YtDlp;

    /// <summary>Raised on a background thread.</summary>
    public event Action<DownloadJob>? JobAdded;
    public event Action<DownloadJob>? JobRemoved;
    public event Action<DownloadJob>? JobFinished;

    /// <summary>A download failed or is being retried (full exception, for the app log / support).</summary>
    public event Action<DownloadJob, Exception>? JobProblem;

    /// <summary>True while any download runs; platforms use it to keep the device awake / show a service.</summary>
    public event Action<bool>? BusyChanged;

    public IReadOnlyList<DownloadJob> Jobs
    {
        get { lock (_lock) return _jobs.ToList(); }
    }

    public bool IsBusy => Jobs.Any(j => j.IsRunning);

    // ---------------------------------------------------------------- commands

    public DownloadJob Add(DownloadJob job, bool startNow)
    {
        if (string.IsNullOrWhiteSpace(job.Folder)) job.Folder = Settings.DownloadFolder;
        if (job.Kind == DownloadKind.Http && !string.IsNullOrWhiteSpace(job.FileName))
        {
            job.FileName = FileNameHelper.Sanitize(job.FileName);
            lock (_lock)
                job.FileName = FileNameHelper.GetUniqueFileName(job.Folder, job.FileName,
                    p => _jobs.Any(j => j.Status != DownloadStatus.Completed && string.Equals(j.FullPath, p, StringComparison.OrdinalIgnoreCase)));
        }
        job.Status = startNow ? DownloadStatus.Queued : DownloadStatus.Paused;
        lock (_lock) _jobs.Add(job);
        JobAdded?.Invoke(job);
        Save();
        Pump();
        return job;
    }

    public void Resume(DownloadJob job)
    {
        if (job.IsRunning || job.Status is DownloadStatus.Completed or DownloadStatus.Queued) return;
        job.Status = DownloadStatus.Queued;
        job.Error = null;
        job.AutoRetries = 0;
        Pump();
    }

    /// <summary>Restarts a download from scratch.</summary>
    public void Redownload(DownloadJob job)
    {
        if (job.IsRunning) return;
        ResetProgress(job);
        if (job.Kind == DownloadKind.Http && File.Exists(job.FullPath))
            job.FileName = FileNameHelper.GetUniqueFileName(job.Folder, job.FileName);
        job.Status = DownloadStatus.Queued;
        job.Error = null;
        job.AutoRetries = 0;
        Pump();
    }

    private static void ResetProgress(DownloadJob job)
    {
        lock (job.Sync)
        {
            job.Segments.Clear();
            job.HlsDone = null;
            job.Parts = null;
        }
        job.TotalBytes = null;
        job.MediaDownloaded = 0;
        job.CompletedUtc = null;
        job.ETag = null;
        job.LastModified = null;
    }

    public void Pause(DownloadJob job)
    {
        job.PausedByFocus = false;
        if (job.IsRunning) job.Cts?.Cancel();
        else if (job.Status == DownloadStatus.Queued) job.Status = DownloadStatus.Paused;
        if (FocusedJobId == job.Id) SetFocus(null);
    }

    public void PauseAll()
    {
        SetFocus(null);
        foreach (var j in Jobs) Pause(j);
    }

    public void ResumeAll()
    {
        foreach (var j in Jobs.Where(j => j.Status is DownloadStatus.Paused or DownloadStatus.Failed))
        {
            j.Status = DownloadStatus.Queued;
            j.Error = null;
            j.AutoRetries = 0;
        }
        Pump();
    }

    public async Task RemoveAsync(DownloadJob job, bool deleteFiles)
    {
        if (FocusedJobId == job.Id) SetFocus(null);
        Pause(job);
        if (job.RunTask is { } t) { try { await t; } catch { } }
        lock (_lock) _jobs.Remove(job);

        switch (job.Kind)
        {
            case DownloadKind.Torrent:
                await Torrents.StopAndRemoveAsync(job.Id, deleteData: deleteFiles);
                if (job.TorrentFile is { } tf) TryDelete(tf);
                break;
            case DownloadKind.Hls:
                TryDeleteDir(job.FullPath + ".bdmparts");
                TryDelete(job.TempPath);
                break;
            case DownloadKind.Media when job.Parts is { } parts:
                foreach (var p in parts) { TryDelete(p.TempPath); TryDelete(p.FullPath); }
                TryDelete(job.TempPath);
                break;
            default:
                TryDelete(job.TempPath);
                break;
        }
        if (deleteFiles && job.Status == DownloadStatus.Completed && job.Kind != DownloadKind.Torrent) TryDelete(job.FullPath);
        JobRemoved?.Invoke(job);
        Save();
    }

    // ---------------------------------------------------------------- focus mode & bandwidth

    /// <summary>The download currently given all the bandwidth, or null.</summary>
    public Guid? FocusedJobId { get; private set; }

    public event Action? FocusChanged;

    /// <summary>
    /// Focus: give one download the full connection. Other HTTP/HLS/torrent downloads are slowed to
    /// <see cref="AppSettings.FocusOthersKBps"/> (keeping their connections) or paused if that is 0;
    /// yt-dlp downloads are paused (they can't change speed while running). Queued downloads wait.
    /// Everything returns to normal when focus is cleared or the focused download stops.
    /// </summary>
    public void SetFocus(DownloadJob? job)
    {
        var previous = FocusedJobId;
        FocusedJobId = job?.Id;
        if (job is not null && !job.IsRunning && job.Status != DownloadStatus.Completed)
        {
            job.Status = DownloadStatus.Queued;
            job.Error = null;
        }
        ApplyBandwidth();

        if (job is null && previous is not null)
        {
            // Resume what focus paused.
            foreach (var j in Jobs.Where(j => j.PausedByFocus))
            {
                j.PausedByFocus = false;
                if (!j.IsRunning && j.Status == DownloadStatus.Paused) j.Status = DownloadStatus.Queued;
            }
        }
        Pump();
        FocusChanged?.Invoke();
    }

    /// <summary>Re-applies speed limits after a setting or focus change.</summary>
    public void ApplyBandwidth()
    {
        GlobalLimiter.BytesPerSecond = Settings.GlobalSpeedLimitKBps * 1024L;
        _ = Torrents.ApplyEngineLimitsAsync();
        var focus = FocusedJobId;
        long slow = Math.Max(0, Settings.FocusOthersKBps) * 1024L;

        foreach (var j in Jobs)
        {
            bool throttle = focus is not null && j.Id != focus;
            long limit = throttle ? slow : 0;
            j.Limiter.BytesPerSecond = limit;
            if (j.Kind == DownloadKind.Torrent) _ = Torrents.SetDownloadLimitAsync(j.Id, throttle ? Math.Max(slow, 1024) : 0);

            bool mustPause = throttle && j.IsRunning && (slow == 0 || (j.Kind == DownloadKind.Media && !UsesSegments(j)));
            if (mustPause)
            {
                j.PausedByFocus = true;
                j.Cts?.Cancel();
            }
        }
    }

    private bool UsesSegments(DownloadJob j) => j.Kind == DownloadKind.Media && !YtDlp.IsAvailable;

    // ---------------------------------------------------------------- settings & tools

    public void SaveSettings()
    {
        ApplyMediaSettings();
        Settings.Save(_settingsFile);
        ApplyBandwidth();
    }

    /// <summary>Where the "Sign in to YouTube" page stores the session (app-private folder).</summary>
    /// <summary>Sign-in sessions (YouTube, Instagram, Facebook) in one cookies.txt (name kept from 1.x for compatibility).</summary>
    public string SignInCookiesPath => Path.Combine(DataDir, "youtube-cookies.txt");

    private void ApplyMediaSettings()
    {
        YtDlp.ForceIPv4 = Settings.ForceIPv4ForVideo;
        YtDlp.CookiesFile = Settings.CookiesFile;
        YtDlp.CookiesFromBrowser = Settings.CookiesFromBrowser;
    }

    /// <summary>Updates yt-dlp once a day (video sites change often). Desktop only.</summary>
    public async Task AutoUpdateYtDlpAsync()
    {
        if (!Tools.IsSupported || !Settings.AutoUpdateYtDlp || DateTime.UtcNow - Settings.LastYtDlpUpdateCheckUtc < TimeSpan.FromDays(1)) return;
        var path = Tools.YtDlpPath;
        if (path is null || !path.StartsWith(Tools.ToolsDir, StringComparison.OrdinalIgnoreCase)) return;
        if (Jobs.Any(j => j.IsRunning && j.Kind == DownloadKind.Media)) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await Tools.UpdateYtDlpAsync(cts.Token);
            Settings.LastYtDlpUpdateCheckUtc = DateTime.UtcNow;
            SaveSettings();
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { }
    }

    // ---------------------------------------------------------------- queue

    /// <summary>Starts queued jobs while below the concurrency limit (the focused job always may start).</summary>
    public void Pump()
    {
        lock (_lock)
        {
            int running = _jobs.Count(j => j.IsRunning && j.Status != DownloadStatus.Seeding);
            var focus = FocusedJobId;
            var queued = _jobs.Where(j => j.Status == DownloadStatus.Queued && !j.IsRunning)
                .OrderByDescending(j => j.Id == focus).ThenBy(j => j.AddedUtc);
            foreach (var job in queued)
            {
                bool isFocus = job.Id == focus;
                if (focus is not null && !isFocus) break;   // others wait while something has focus
                if (!isFocus && running >= Math.Max(1, Settings.MaxConcurrentDownloads)) break;
                job.Cts = new CancellationTokenSource();
                job.Status = DownloadStatus.Connecting;
                var token = job.Cts.Token;
                job.RunTask = Task.Run(() => RunAsync(job, token));
                running++;
            }
        }
        RaiseBusyIfChanged();
    }

    private IReadOnlyList<RateLimiter> LimitersFor(DownloadJob job) => [job.Limiter, GlobalLimiter];

    private async Task RunAsync(DownloadJob job, CancellationToken ct)
    {
        ApplyBandwidth();
        try
        {
            job.Error = null;
            job.NeedsSignIn = false;
            int connections = Math.Clamp(Settings.Connections, 1, 32);
            switch (job.Kind)
            {
                case DownloadKind.Media:
                    if (Tools.IsSupported && !YtDlp.IsAvailable) await EnsureVideoToolsAsync(job, ct);
                    await MediaFor(job.Url).DownloadAsync(job, connections, LimitersFor(job), ct);
                    break;
                case DownloadKind.Hls:
                    await new HlsDownloader(Http) { Connections = connections, Limiters = LimitersFor(job) }
                        .DownloadAsync(job, MaxHeight(job.MediaQuality), ct);
                    break;
                case DownloadKind.Torrent:
                    await Torrents.DownloadAsync(job, job.Limiter.BytesPerSecond, ct);
                    break;
                default:
                    await new SegmentedDownloader(Http) { Connections = connections, Limiters = LimitersFor(job) }.DownloadAsync(job, ct);
                    break;
            }
            job.Status = DownloadStatus.Completed;
            job.CompletedUtc = DateTime.UtcNow;
            job.AutoRetries = 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            job.Status = DownloadStatus.Paused;
        }
        catch (RemoteFileChangedException ex) when (job.AutoRetries == 0)
        {
            // The server has a new version of the file: the saved parts are useless, start over once.
            ResetProgress(job);
            job.AutoRetries++;
            job.Error = ex.Message + " Restarting from the beginning.";
            job.Status = DownloadStatus.Queued;
        }
        catch (Exception ex) when (SegmentedDownloader.IsTransientError(ex) && job.AutoRetries < Settings.AutoRetryCount)
        {
            // Long downloads: network dropped, Wi-Fi changed, PC slept... wait and try again automatically.
            JobProblem?.Invoke(job, ex);
            job.AutoRetries++;
            job.Error = $"{ex.Message} Retrying ({job.AutoRetries} of {Settings.AutoRetryCount})...";
            job.Status = DownloadStatus.Paused;
            var delay = TimeSpan.FromSeconds(Math.Min(300, 10 * Math.Pow(2, Math.Min(job.AutoRetries, 5))));
            _ = Task.Delay(delay).ContinueWith(_ =>
            {
                if (job.Status == DownloadStatus.Paused && !job.IsRunning && job.Error is not null) { job.Status = DownloadStatus.Queued; Pump(); }
            }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            JobProblem?.Invoke(job, ex);
            job.Status = DownloadStatus.Failed;
            job.Error = ex.Message;
            job.NeedsSignIn = ex is MediaSignInRequiredException;
        }
        finally
        {
            job.Stage = null;
            var cts = job.Cts;
            job.Cts = null;
            cts?.Dispose();
            if (job.PausedByFocus && job.Status != DownloadStatus.Completed) job.Status = DownloadStatus.Paused;
            Save();
            if (job.Status is DownloadStatus.Completed or DownloadStatus.Failed)
            {
                if (FocusedJobId == job.Id) SetFocus(null);
                JobFinished?.Invoke(job);
            }
            Pump();
        }
    }

    private static int? MaxHeight(string? quality) => int.TryParse(quality, out var h) ? h : null;

    // ---------------------------------------------------------------- persistence

    private readonly object _saveLock = new();

    private void Tick()
    {
        if (IsBusy) Save();
        RaiseBusyIfChanged();
    }

    private void RaiseBusyIfChanged()
    {
        bool busy = IsBusy;
        if (busy == _wasBusy) return;
        _wasBusy = busy;
        BusyChanged?.Invoke(busy);
    }

    public void Save()
    {
        List<DownloadJob> snapshot;
        lock (_lock) snapshot = _jobs.Select(j => j.CloneForSave()).ToList();
        lock (_saveLock)
        {
            try { AtomicFile.WriteAllText(_jobsFile, JsonSerializer.Serialize(snapshot, AppSettings.JsonOptions)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void LoadJobs()
    {
        try
        {
            if (!File.Exists(_jobsFile)) return;
            var jobs = JsonSerializer.Deserialize<List<DownloadJob>>(File.ReadAllText(_jobsFile), AppSettings.JsonOptions) ?? new();
            foreach (var j in jobs)
            {
                // Anything that was running when the app closed is resumable, not auto-started.
                if (j.Status is DownloadStatus.Connecting or DownloadStatus.Downloading or DownloadStatus.Queued or DownloadStatus.Seeding)
                    j.Status = j.Status == DownloadStatus.Seeding ? DownloadStatus.Completed : DownloadStatus.Paused;
            }
            _jobs.AddRange(jobs);
        }
        catch (JsonException) { }
        catch (IOException) { }
    }

    /// <summary>Stops all downloads (keeping progress) and saves state.</summary>
    public void Shutdown(TimeSpan wait)
    {
        _autosave.Dispose();
        var running = Jobs.Where(j => j.IsRunning).Select(j => { j.Cts?.Cancel(); return j.RunTask; }).OfType<Task>().ToArray();
        try { Task.WaitAll(running, wait); } catch (AggregateException) { }
        Save();
        try { Torrents.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(10)); } catch (AggregateException) { }
    }

    public void Dispose()
    {
        Shutdown(TimeSpan.FromSeconds(3));
        Http.Dispose();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
