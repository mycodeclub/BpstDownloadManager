using System.Buffers;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Bdm.Core.Http;

namespace Bdm.Core.Hls;

/// <summary>
/// Built-in HLS (.m3u8) downloader, so streamed videos (what "blob:" players load) can be saved on every
/// platform without external tools. Segments are fetched in parallel, saved one file each (so a stopped
/// download resumes per segment), AES-128 segments are decrypted, and the parts are joined into one file:
/// MPEG-TS (.ts) for classic streams, fragmented MP4 (.mp4) when the stream uses an init segment.
/// </summary>
public sealed class HlsDownloader
{
    private readonly HttpClient _http;

    public HlsDownloader(HttpClient http) => _http = http;

    public int Connections { get; init; } = 8;
    public int MaxRetries { get; init; } = 6;
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public IReadOnlyList<RateLimiter> Limiters { get; init; } = [];

    /// <param name="maxHeight">Highest video height to pick from a master playlist (null = best).</param>
    public async Task DownloadAsync(DownloadJob job, int? maxHeight, CancellationToken ct)
    {
        Directory.CreateDirectory(job.Folder);
        job.Status = DownloadStatus.Connecting;
        job.Stage = "Reading stream playlist";

        var playlist = await LoadPlaylistAsync(job, new Uri(job.Url), ct);
        if (playlist.IsMaster)
        {
            var v = playlist.PickVariant(maxHeight);
            job.Detail = v.Resolution is { } r ? $"Quality {r}" : null;
            playlist = await LoadPlaylistAsync(job, v.Uri, ct);
        }
        if (playlist.IsEndless)
            throw new NotSupportedException("This is a live stream (it never ends), which can't be saved as a file yet.");
        if (playlist.Segments.Count == 0)
            throw new InvalidDataException("The stream playlist has no segments.");
        if (playlist.Segments.Any(s => s.Key is { Method: not "AES-128" }))
            throw new NotSupportedException("This stream is DRM-protected (SAMPLE-AES or similar), so it can't be downloaded.");

        bool fmp4 = playlist.InitSegment is not null ||
                    playlist.Segments[0].Uri.AbsolutePath.EndsWith(".m4s", StringComparison.OrdinalIgnoreCase);
        var ext = fmp4 ? ".mp4" : ".ts";

        bool fresh = job.HlsDone is null || job.HlsSegmentCount != playlist.Segments.Count;
        if (fresh)
        {
            var stem = FileNameHelper.Sanitize(string.IsNullOrWhiteSpace(job.OutputName)
                ? Path.GetFileNameWithoutExtension(string.IsNullOrWhiteSpace(job.FileName) ? "video" : job.FileName)
                : job.OutputName);
            job.FileName = FileNameHelper.GetUniqueFileName(job.Folder, stem + ext);
            job.HlsSegmentCount = playlist.Segments.Count;
            lock (job.Sync) job.HlsDone = new List<int>();
        }

        var partDir = job.FullPath + ".bdmparts";
        Directory.CreateDirectory(partDir);
        var done = new HashSet<int>();
        long bytesSoFar = 0;
        lock (job.Sync)
        {
            foreach (var i in job.HlsDone!)
            {
                var f = PartPath(partDir, i);
                if (File.Exists(f)) { done.Add(i); bytesSoFar += new FileInfo(f).Length; }
            }
            job.HlsDone = done.ToList();
        }
        job.MediaDownloaded = bytesSoFar;

        job.Status = DownloadStatus.Downloading;
        job.Stage = null;
        var keys = new Dictionary<Uri, byte[]>();
        var keyLock = new SemaphoreSlim(1, 1);
        var queue = new Queue<HlsSegment>(playlist.Segments.Where(s => !done.Contains(s.Index)));
        var queueLock = new object();
        int count = playlist.Segments.Count;

        void UpdateEstimate()
        {
            int have;
            lock (job.Sync) have = job.HlsDone!.Count;
            if (have > 0) job.TotalBytes = Math.Max(job.MediaDownloaded, job.MediaDownloaded * count / have);
            job.Detail = $"{have} of {count} parts";
        }
        UpdateEstimate();

        async Task Worker()
        {
            while (true)
            {
                HlsSegment seg;
                lock (queueLock)
                {
                    if (queue.Count == 0) return;
                    seg = queue.Dequeue();
                }
                byte[] data = await WithRetryAsync(() => FetchAsync(job, seg.Uri, seg.ByteRange, ct), ct);
                if (seg.Key is { Method: "AES-128" } key)
                    data = Decrypt(data, await GetKeyAsync(job, key, keys, keyLock, ct), key.Iv ?? SequenceIv(seg.MediaSequence));

                var path = PartPath(partDir, seg.Index);
                await File.WriteAllBytesAsync(path + ".tmp", data, ct);
                File.Move(path + ".tmp", path, overwrite: true);
                lock (job.Sync) job.HlsDone!.Add(seg.Index);
                UpdateEstimate();
            }
        }

        var workers = Enumerable.Range(0, Math.Clamp(Connections, 1, 16)).Select(_ => Task.Run(Worker, ct)).ToArray();
        await Task.WhenAll(workers);

        // Join the parts in order.
        job.Stage = "Joining parts";
        var temp = job.FullPath + ".bdmpart";
        await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
        {
            if (playlist.InitSegment is { } init)
            {
                var initBytes = await WithRetryAsync(() => FetchAsync(job, init, playlist.InitByteRange, ct), ct);
                await output.WriteAsync(initBytes, ct);
            }
            for (int i = 0; i < count; i++)
            {
                await using var part = File.OpenRead(PartPath(partDir, i));
                await part.CopyToAsync(output, ct);
            }
        }
        if (File.Exists(job.FullPath)) job.FileName = FileNameHelper.GetUniqueFileName(job.Folder, job.FileName);
        File.Move(temp, job.FullPath);
        try { Directory.Delete(partDir, recursive: true); } catch (IOException) { }
        job.TotalBytes = new FileInfo(job.FullPath).Length;
        job.MediaDownloaded = job.TotalBytes.Value;
        job.Stage = null;
        job.Detail = $"{count} parts";
    }

    public static string PartPath(string partDir, int index) => Path.Combine(partDir, $"{index:D6}.seg");

    private async Task<HlsPlaylist> LoadPlaylistAsync(DownloadJob job, Uri uri, CancellationToken ct)
    {
        var bytes = await WithRetryAsync(() => FetchAsync(job, uri, null, ct, throttle: false), ct);
        return HlsPlaylist.Parse(System.Text.Encoding.UTF8.GetString(bytes), uri);
    }

    private async Task<byte[]> GetKeyAsync(DownloadJob job, HlsKey key, Dictionary<Uri, byte[]> cache, SemaphoreSlim gate, CancellationToken ct)
    {
        if (key.Uri is null) throw new InvalidDataException("Encrypted stream without a key address.");
        await gate.WaitAsync(ct);
        try
        {
            if (!cache.TryGetValue(key.Uri, out var k))
            {
                k = await WithRetryAsync(() => FetchAsync(job, key.Uri, null, ct, throttle: false), ct);
                if (k.Length != 16) throw new InvalidDataException("The stream's decryption key is not a 16-byte AES key.");
                cache[key.Uri] = k;
            }
            return k;
        }
        finally
        {
            gate.Release();
        }
    }

    internal static byte[] SequenceIv(long sequence)
    {
        var iv = new byte[16];
        for (int i = 15; i >= 8; i--) { iv[i] = (byte)(sequence & 0xFF); sequence >>= 8; }
        return iv;
    }

    internal static byte[] Decrypt(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptCbc(data, iv, PaddingMode.PKCS7);
    }

    private async Task<byte[]> FetchAsync(DownloadJob job, Uri uri, (long Length, long Offset)? range, CancellationToken ct, bool throttle = true)
    {
        using var req = HttpFactory.CreateRequest(uri.ToString(), job.Referrer, job.Headers);
        if (range is { } r) req.Headers.Range = new RangeHeaderValue(r.Offset, r.Offset + r.Length - 1);
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(StallTimeout);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}", null, resp.StatusCode);

        await using var stream = await resp.Content.ReadAsStreamAsync(stall.Token);
        using var ms = new MemoryStream(resp.Content.Headers.ContentLength is long len and < int.MaxValue ? (int)len : 1 << 20);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                stall.CancelAfter(StallTimeout);
                int n = await stream.ReadAsync(buffer.AsMemory(0, 64 * 1024), stall.Token);
                if (n == 0) break;
                ms.Write(buffer, 0, n);
                if (throttle)
                {
                    lock (job.Sync) job.MediaDownloaded += n;
                    foreach (var l in Limiters) await l.ConsumeAsync(n, ct);
                }
            }
        }
        catch
        {
            if (throttle) lock (job.Sync) job.MediaDownloaded -= ms.Length; // this attempt's bytes are discarded
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return ms.ToArray();
    }

    private async Task<T> WithRetryAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (SegmentedDownloader.IsTransientError(ex) && attempt < MaxRetries)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << attempt)), ct);
            }
        }
    }
}
