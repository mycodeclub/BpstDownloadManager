using System.Text.Json;
using BpstVideoDownloader.Core.Http;
using BpstVideoDownloader.Core.Media;

namespace BpstVideoDownloader.Core;

/// <summary>Owns the download list, the queue (max concurrent downloads) and persistence.</summary>
public sealed class DownloadManager : IDisposable
{
    private readonly List<DownloadJob> _jobs = new();
    private readonly object _lock = new();
    private readonly string _jobsFile;
    private readonly string _settingsFile;
    private readonly Timer _autosave;

    public DownloadManager(string dataDir)
    {
        DataDir = dataDir;
        Directory.CreateDirectory(dataDir);
        _jobsFile = Path.Combine(dataDir, "downloads.json");
        _settingsFile = Path.Combine(dataDir, "settings.json");
        Settings = AppSettings.Load(_settingsFile);
        // After moving the data folder (VeloxDM rename) the saved sign-in path points at the old location.
        if (Settings.CookiesFile is { } cf && !File.Exists(cf) &&
            Path.GetFileName(cf).Equals("youtube-cookies.txt", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(YouTubeCookiesPath))
        {
            Settings.CookiesFile = YouTubeCookiesPath;
            Settings.Save(_settingsFile);
        }
        Http = HttpFactory.CreateClient();
        Tools = new MediaTools(Path.Combine(dataDir, "tools"));
        Media = new MediaDownloader(Tools);
        ApplyMediaSettings();
        LoadJobs();
        _autosave = new Timer(_ => { if (Jobs.Any(j => j.IsRunning)) Save(); }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    public static string DefaultDataDir => LazyDataDir.Value;

    private static readonly Lazy<string> LazyDataDir = new(() =>
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(local, "BpstVideoDownloader");
        // The app was called VeloxDM before; carry its downloads list, settings, tools and sign-in over once.
        var legacy = Path.Combine(local, "VeloxDM");
        bool newHasData = File.Exists(Path.Combine(dir, "settings.json")) || File.Exists(Path.Combine(dir, "downloads.json"));
        if (!newHasData && Directory.Exists(legacy)) MigrateLegacyData(legacy, dir);
        return dir;
    });

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

        var oldCookies = Path.Combine(legacy, "youtube-cookies.txt");
        var settingsPath = Path.Combine(dir, "settings.json");
        if (File.Exists(settingsPath))
        {
            // Saved paths (sign-in file) pointed into the old folder.
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

    public string DataDir { get; }
    public AppSettings Settings { get; }
    public HttpClient Http { get; }
    public MediaTools Tools { get; }
    public MediaDownloader Media { get; }

    /// <summary>Raised on a background thread.</summary>
    public event Action<DownloadJob>? JobAdded;
    public event Action<DownloadJob>? JobRemoved;
    public event Action<DownloadJob>? JobFinished;

    public IReadOnlyList<DownloadJob> Jobs
    {
        get { lock (_lock) return _jobs.ToList(); }
    }

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
        Pump();
    }

    /// <summary>Restarts a completed or failed download from scratch.</summary>
    public void Redownload(DownloadJob job)
    {
        if (job.IsRunning) return;
        lock (job.Sync) job.Segments.Clear();
        job.TotalBytes = null;
        job.MediaDownloaded = 0;
        job.CompletedUtc = null;
        if (job.Kind == DownloadKind.Http && File.Exists(job.FullPath))
            job.FileName = FileNameHelper.GetUniqueFileName(job.Folder, job.FileName);
        job.Status = DownloadStatus.Queued;
        job.Error = null;
        Pump();
    }

    public void Pause(DownloadJob job)
    {
        if (job.IsRunning) job.Cts?.Cancel();
        else if (job.Status == DownloadStatus.Queued) job.Status = DownloadStatus.Paused;
    }

    public void PauseAll()
    {
        foreach (var j in Jobs) Pause(j);
    }

    public void ResumeAll()
    {
        foreach (var j in Jobs.Where(j => j.Status is DownloadStatus.Paused or DownloadStatus.Failed))
        {
            j.Status = DownloadStatus.Queued;
            j.Error = null;
        }
        Pump();
    }

    public async Task RemoveAsync(DownloadJob job, bool deleteFiles)
    {
        Pause(job);
        if (job.RunTask is { } t) { try { await t; } catch { } }
        lock (_lock) _jobs.Remove(job);
        if (job.Kind == DownloadKind.Http) TryDelete(job.TempPath);
        if (deleteFiles && job.Status == DownloadStatus.Completed) TryDelete(job.FullPath);
        JobRemoved?.Invoke(job);
        Save();
    }

    public void SaveSettings()
    {
        ApplyMediaSettings();
        Settings.Save(_settingsFile);
    }

    /// <summary>Where the "Sign in to YouTube" window stores the session (user-only folder).</summary>
    public string YouTubeCookiesPath => Path.Combine(DataDir, "youtube-cookies.txt");

    public bool IsYouTubeSignedIn => File.Exists(YouTubeCookiesPath) &&
        string.Equals(Settings.CookiesFile, YouTubeCookiesPath, StringComparison.OrdinalIgnoreCase);

    private void ApplyMediaSettings()
    {
        Media.ForceIPv4 = Settings.ForceIPv4ForVideo;
        Media.CookiesFile = Settings.CookiesFile;
        Media.CookiesFromBrowser = Settings.CookiesFromBrowser;
    }

    /// <summary>
    /// Video sites change often and old yt-dlp builds stop working, so update it once a day
    /// (only the copy in BpstVideoDownloader's own tools folder; a yt-dlp found on PATH is left alone).
    /// </summary>
    public async Task AutoUpdateYtDlpAsync()
    {
        if (!Settings.AutoUpdateYtDlp || DateTime.UtcNow - Settings.LastYtDlpUpdateCheckUtc < TimeSpan.FromDays(1)) return;
        var path = Tools.YtDlpPath;
        if (path is null || !path.StartsWith(Tools.ToolsDir, StringComparison.OrdinalIgnoreCase)) return;
        if (Jobs.Any(j => j.IsRunning && j.Kind == DownloadKind.Media)) return; // can't replace the exe while it runs
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await Tools.UpdateYtDlpAsync(cts.Token);
            Settings.LastYtDlpUpdateCheckUtc = DateTime.UtcNow;
            SaveSettings();
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { }
    }

    /// <summary>Starts queued jobs while below the concurrency limit.</summary>
    public void Pump()
    {
        lock (_lock)
        {
            int running = _jobs.Count(j => j.IsRunning);
            foreach (var job in _jobs.Where(j => j.Status == DownloadStatus.Queued && !j.IsRunning).OrderBy(j => j.AddedUtc))
            {
                if (running >= Math.Max(1, Settings.MaxConcurrentDownloads)) break;
                job.Cts = new CancellationTokenSource();
                job.Status = DownloadStatus.Connecting;
                var token = job.Cts.Token;
                job.RunTask = Task.Run(() => RunAsync(job, token));
                running++;
            }
        }
    }

    private async Task RunAsync(DownloadJob job, CancellationToken ct)
    {
        try
        {
            job.Error = null;
            job.NeedsSignIn = false;
            if (job.Kind == DownloadKind.Media)
            {
                await Media.DownloadAsync(job, Settings.Connections, ct);
            }
            else
            {
                var dl = new SegmentedDownloader(Http) { Connections = Math.Clamp(Settings.Connections, 1, 32) };
                await dl.DownloadAsync(job, ct);
            }
            job.Status = DownloadStatus.Completed;
            job.CompletedUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            job.Status = DownloadStatus.Paused;
        }
        catch (Exception ex)
        {
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
            Save();
            if (job.Status is DownloadStatus.Completed or DownloadStatus.Failed) JobFinished?.Invoke(job);
            Pump();
        }
    }

    // ---------------------------------------------------------------- persistence

    private readonly object _saveLock = new();

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
                if (j.Status is DownloadStatus.Connecting or DownloadStatus.Downloading or DownloadStatus.Queued)
                    j.Status = DownloadStatus.Paused;
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
}
