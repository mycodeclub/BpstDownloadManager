using System.Net;
using MonoTorrent;
using MonoTorrent.Client;

namespace Bdm.Core.Torrent;

public sealed record TorrentFileEntry(string Path, long Length);

public sealed record TorrentMetaInfo(string Name, long Size, IReadOnlyList<TorrentFileEntry> Files);

/// <summary>
/// BitTorrent downloads (magnet links and .torrent files) using MonoTorrent: DHT, peer exchange,
/// trackers, UPnP/NAT-PMP port forwarding and fast resume. One engine is shared by all torrent jobs.
/// </summary>
public sealed class TorrentService : IAsyncDisposable
{
    private readonly string _dataDir;
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, TorrentManager> _managers = new();
    private ClientEngine? _engine;

    public TorrentService(string dataDir, AppSettings settings)
    {
        _dataDir = dataDir;
        _settings = settings;
    }

    public string TorrentsDir => Path.Combine(_dataDir, "torrents");

    /// <summary>Reads a .torrent file (for showing its name, size and file list before adding).</summary>
    public static async Task<TorrentMetaInfo> ReadAsync(string torrentPath)
    {
        var t = await MonoTorrent.Torrent.LoadAsync(torrentPath);
        return new TorrentMetaInfo(t.Name, t.Size, t.Files.Select(f => new TorrentFileEntry(f.Path, f.Length)).ToList());
    }

    /// <summary>Copies a .torrent into the app's folder so the job doesn't depend on the original file.</summary>
    public string Import(string torrentPath, Guid jobId)
    {
        Directory.CreateDirectory(TorrentsDir);
        var dest = Path.Combine(TorrentsDir, jobId.ToString("N") + ".torrent");
        File.Copy(torrentPath, dest, overwrite: true);
        return dest;
    }

    private async Task<ClientEngine> GetEngineAsync()
    {
        if (_engine is not null) return _engine;
        var cache = Path.Combine(_dataDir, "torrent-cache");
        Directory.CreateDirectory(cache);
        var port = _settings.TorrentPort is > 0 and < 65536 ? _settings.TorrentPort : 55125;
        var builder = new EngineSettingsBuilder
        {
            CacheDirectory = cache,
            AllowPortForwarding = _settings.TorrentPortForwarding,
            AutoSaveLoadDhtCache = true,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadMagnetLinkMetadata = true,
            ListenEndPoints = new Dictionary<string, IPEndPoint>
            {
                ["ipv4"] = new IPEndPoint(IPAddress.Any, port),
                ["ipv6"] = new IPEndPoint(IPAddress.IPv6Any, port),
            },
            DhtEndPoint = _settings.TorrentDht ? new IPEndPoint(IPAddress.Any, port) : null,
            MaximumUploadRate = _settings.TorrentUploadLimitKBps * 1024,
            MaximumDownloadRate = _settings.GlobalSpeedLimitKBps * 1024,
        };
        _engine = new ClientEngine(builder.ToSettings());
        return _engine;
    }

    /// <summary>Applies the global download limit (and upload limit) to the engine.</summary>
    public async Task ApplyEngineLimitsAsync()
    {
        if (_engine is null) return;
        var b = new EngineSettingsBuilder(_engine.Settings)
        {
            MaximumDownloadRate = _settings.GlobalSpeedLimitKBps * 1024,
            MaximumUploadRate = _settings.TorrentUploadLimitKBps * 1024,
        };
        await _engine.UpdateSettingsAsync(b.ToSettings());
    }

    /// <summary>Per-torrent download limit (used by Focus mode). 0 = unlimited.</summary>
    public async Task SetDownloadLimitAsync(Guid jobId, long bytesPerSecond)
    {
        TorrentManager? m;
        lock (_managers) _managers.TryGetValue(jobId, out m);
        if (m is null) return;
        var b = new TorrentSettingsBuilder(m.Settings) { MaximumDownloadRate = (int)Math.Min(int.MaxValue, bytesPerSecond) };
        await m.UpdateSettingsAsync(b.ToSettings());
    }

    public async Task DownloadAsync(DownloadJob job, long downloadLimit, CancellationToken ct)
    {
        Directory.CreateDirectory(job.Folder);
        job.Status = DownloadStatus.Connecting;
        job.Stage = "Starting torrent";

        var manager = await GetOrAddAsync(job);
        await SetDownloadLimitAsync(job.Id, downloadLimit);
        long uploadedBefore = job.TorrentUploaded;
        try
        {
            if (manager.State is TorrentState.Stopped or TorrentState.Error)
                await manager.StartAsync();

            if (!manager.HasMetadata)
            {
                job.Stage = "Getting torrent info from peers";
                await manager.WaitForMetadataAsync(ct);
            }
            await OnMetadataAsync(job, manager);

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (manager.State == TorrentState.Error)
                    throw new IOException("Torrent error: " + (manager.Error?.Exception?.Message ?? "unknown"));

                long wanted = WantedSize(manager);
                job.TotalBytes = wanted;
                job.MediaDownloaded = (long)(manager.PartialProgress / 100.0 * wanted);
                job.TorrentUploaded = uploadedBefore + manager.Monitor.DataBytesSent;
                job.Detail = $"{manager.Peers.Seeds} seeds · {manager.Peers.Leechs} peers · ↑ {Format.Speed(manager.Monitor.UploadRate)}";

                bool downloaded = manager.PartialProgress >= 100.0 || manager.State == TorrentState.Seeding;
                if (downloaded)
                {
                    job.MediaDownloaded = wanted;
                    double ratio = wanted > 0 ? (double)job.TorrentUploaded / wanted : 0;
                    if (_settings.TorrentSeedRatio <= 0 || ratio >= _settings.TorrentSeedRatio) break;
                    job.Status = DownloadStatus.Seeding;
                    job.Stage = $"Seeding (ratio {ratio:0.00} of {_settings.TorrentSeedRatio:0.00})";
                }
                else
                {
                    job.Status = DownloadStatus.Downloading;
                    job.Stage = manager.State switch
                    {
                        TorrentState.Hashing => "Checking existing data",
                        TorrentState.Metadata => "Getting torrent info from peers",
                        _ when manager.OpenConnections == 0 => "Looking for peers",
                        _ => null,
                    };
                }
                await Task.Delay(1000, ct);
            }

            // Finished: stop and forget the torrent (data stays on disk).
            await StopAndRemoveAsync(job.Id, deleteData: false);
            job.Stage = null;
        }
        catch (OperationCanceledException)
        {
            try { await manager.StopAsync(TimeSpan.FromSeconds(10)); } catch (Exception) { }
            throw;
        }
    }

    private static long WantedSize(TorrentManager m) =>
        m.Files.Where(f => f.Priority != Priority.DoNotDownload).Sum(f => f.Length);

    private async Task OnMetadataAsync(DownloadJob job, TorrentManager manager)
    {
        job.FileName = manager.Torrent!.Name;
        if (job.TorrentFile is null && File.Exists(manager.MetadataPath))
        {
            Directory.CreateDirectory(TorrentsDir);
            var dest = Path.Combine(TorrentsDir, job.Id.ToString("N") + ".torrent");
            File.Copy(manager.MetadataPath, dest, overwrite: true);
            job.TorrentFile = dest;
        }
        if (job.TorrentSkippedFiles is { Count: > 0 } skipped)
        {
            var set = new HashSet<string>(skipped, StringComparer.Ordinal);
            foreach (var f in manager.Files)
                if (set.Contains(f.Path)) await manager.SetFilePriorityAsync(f, Priority.DoNotDownload);
        }
        job.TotalBytes = WantedSize(manager);
    }

    private async Task<TorrentManager> GetOrAddAsync(DownloadJob job)
    {
        lock (_managers)
            if (_managers.TryGetValue(job.Id, out var existing)) return existing;

        await _gate.WaitAsync();
        try
        {
            var engine = await GetEngineAsync();
            var ts = new TorrentSettingsBuilder { AllowDht = _settings.TorrentDht, AllowPeerExchange = true, CreateContainingDirectory = true }.ToSettings();
            TorrentManager manager;
            if (job.TorrentFile is { } file && File.Exists(file))
                manager = await engine.AddAsync(file, job.Folder, ts);
            else if (MagnetLink.TryParse(job.Url, out var magnet))
                manager = await engine.AddAsync(magnet, job.Folder, ts);
            else
                throw new InvalidDataException("This isn't a valid magnet link or .torrent file.");
            lock (_managers) _managers[job.Id] = manager;
            return manager;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops a torrent and removes it from the engine (optionally deleting downloaded data).</summary>
    public async Task StopAndRemoveAsync(Guid jobId, bool deleteData)
    {
        TorrentManager? m;
        lock (_managers)
        {
            _managers.TryGetValue(jobId, out m);
            _managers.Remove(jobId);
        }
        if (m is null || _engine is null) return;
        try { await m.StopAsync(TimeSpan.FromSeconds(10)); } catch (Exception) { }
        await _engine.RemoveAsync(m, deleteData ? RemoveMode.CacheDataAndDownloadedData : RemoveMode.CacheDataOnly);
    }

    public async ValueTask DisposeAsync()
    {
        if (_engine is null) return;
        try { await _engine.StopAllAsync(TimeSpan.FromSeconds(10)); } catch (Exception) { }
        _engine.Dispose();
        _engine = null;
    }
}
