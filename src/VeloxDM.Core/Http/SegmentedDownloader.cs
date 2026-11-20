using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VeloxDM.Core.Http;

/// <summary>
/// Multi-connection HTTP downloader.
/// <list type="bullet">
/// <item>The file is split into byte ranges, each fetched over its own connection.</item>
/// <item>When a connection finishes early it takes over half of the largest remaining range
/// (dynamic segmentation), so all connections stay busy until the very end.</item>
/// <item>Ranges are written in place into a pre-sized sparse file, and progress is stored
/// per segment, so a paused or crashed download resumes where it left off.</item>
/// </list>
/// </summary>
public sealed class SegmentedDownloader
{
    private const int BufferSize = 128 * 1024;
    private readonly HttpClient _http;

    public SegmentedDownloader(HttpClient http) => _http = http;

    public int Connections { get; init; } = 8;
    public long MinSegmentSize { get; init; } = 256 * 1024;
    public int MaxRetries { get; init; } = 6;
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public async Task DownloadAsync(DownloadJob job, CancellationToken ct)
    {
        Directory.CreateDirectory(job.Folder);
        job.Status = DownloadStatus.Connecting;

        bool resumable = job.Segments.Count > 0 && job.TotalBytes is not null && File.Exists(job.TempPath);
        if (!resumable)
        {
            var probe = await HttpProbe.ProbeAsync(_http, job.Url, job.Referrer, ct);
            job.TotalBytes = probe.TotalBytes;
            job.SupportsRanges = probe.SupportsRanges && probe.TotalBytes > 0;
            if (string.IsNullOrWhiteSpace(job.FileName))
                job.FileName = FileNameHelper.GetUniqueFileName(job.Folder, probe.FileName);
            TryDelete(job.TempPath);
            lock (job.Sync) job.Segments = CreateInitialSegments(job);
        }

        job.Status = DownloadStatus.Downloading;
        if (job.SupportsRanges && job.TotalBytes is long total)
            await DownloadSegmentedAsync(job, total, ct);
        else
            await DownloadSingleStreamAsync(job, ct);

        // Move the finished temp file into place, avoiding clobbering a file created meanwhile.
        if (File.Exists(job.FullPath))
            job.FileName = FileNameHelper.GetUniqueFileName(job.Folder, job.FileName);
        File.Move(job.TempPath, job.FullPath);
    }

    private List<Segment> CreateInitialSegments(DownloadJob job)
    {
        if (!job.SupportsRanges || job.TotalBytes is not long total)
            return [new Segment { Start = 0, End = job.TotalBytes is > 0 ? job.TotalBytes.Value - 1 : Segment.UnknownEnd }];

        int count = (int)Math.Clamp(total / MinSegmentSize, 1, Math.Max(1, Connections));
        long size = total / count;
        var list = new List<Segment>(count);
        for (int i = 0; i < count; i++)
        {
            long start = i * size;
            long end = i == count - 1 ? total - 1 : start + size - 1;
            list.Add(new Segment { Start = start, End = end });
        }
        return list;
    }

    // ---------------------------------------------------------------- multi-connection

    private async Task DownloadSegmentedAsync(DownloadJob job, long total, CancellationToken ct)
    {
        using (var fs = new FileStream(job.TempPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
        {
            // Sparse: writing near the end of a huge file must not force Windows to zero-fill everything before it.
            NativeMethods.TryMakeSparse(fs.SafeFileHandle);
            if (fs.Length != total) fs.SetLength(total);
        }

        using var handle = File.OpenHandle(job.TempPath, FileMode.Open, FileAccess.Write, FileShare.Read, FileOptions.Asynchronous);

        // Round 1 uses all connections. If servers shed connections (429/503) and work is left over,
        // follow-up rounds continue with a single connection.
        for (int round = 0; round < 4; round++)
        {
            int workers = round == 0 ? Math.Max(1, Connections) : 1;
            using var roundCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(() => WorkerAsync(job, handle, roundCts.Token))).ToArray();
            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                roundCts.Cancel();
                try { await Task.WhenAll(tasks); } catch { /* surface the first error below */ }
                ct.ThrowIfCancellationRequested();
                throw tasks.Select(t => t.Exception?.InnerException).FirstOrDefault(e => e is not null and not OperationCanceledException)
                      ?? new IOException("Download failed.");
            }

            lock (job.Sync)
                if (job.Segments.All(s => s.IsDone)) return;
        }
        throw new IOException("The server kept refusing connections. Try again later or lower the connection count.");
    }

    private async Task WorkerAsync(DownloadJob job, SafeFileHandle handle, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var seg = AcquireSegment(job);
                if (seg is null) return;
                try
                {
                    await DownloadSegmentWithRetryAsync(job, seg, handle, buffer, ct);
                }
                catch (RetireWorkerException)
                {
                    return; // server is limiting connections; the remaining workers take over
                }
                finally
                {
                    lock (job.Sync) seg.Active = false;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Picks an idle unfinished segment, or splits the largest active one in half.</summary>
    private Segment? AcquireSegment(DownloadJob job)
    {
        lock (job.Sync)
        {
            var free = job.Segments.FirstOrDefault(s => !s.Active && !s.IsDone);
            if (free is not null) { free.Active = true; return free; }

            Segment? largest = null;
            foreach (var s in job.Segments)
                if (s.Active && !s.IsDone && (largest is null || s.Remaining > largest.Remaining))
                    largest = s;

            if (largest is null || largest.Remaining < 2 * MinSegmentSize) return null;

            long mid = largest.Position + largest.Remaining / 2;
            var split = new Segment { Start = mid, End = largest.End, Active = true };
            largest.End = mid - 1; // the owning worker notices and stops at the new boundary
            job.Segments.Insert(job.Segments.IndexOf(largest) + 1, split);
            return split;
        }
    }

    private async Task DownloadSegmentWithRetryAsync(DownloadJob job, Segment seg, SafeFileHandle handle, byte[] buffer, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await DownloadSegmentOnceAsync(job, seg, handle, buffer, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
                                                  && OtherWorkersActive(job, seg))
            {
                throw new RetireWorkerException();
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < MaxRetries)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << attempt)), ct);
            }
        }
    }

    private async Task DownloadSegmentOnceAsync(DownloadJob job, Segment seg, SafeFileHandle handle, byte[] buffer, CancellationToken ct)
    {
        long pos, end;
        lock (job.Sync)
        {
            if (seg.IsDone) return;
            pos = seg.Position;
            end = seg.End;
        }

        using var req = HttpFactory.CreateRequest(job.Url, job.Referrer);
        req.Headers.Range = new RangeHeaderValue(pos, end);

        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(StallTimeout);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stall.Token);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}", null, resp.StatusCode);
        if (resp.StatusCode != HttpStatusCode.PartialContent)
            throw new InvalidDataException("The server stopped honouring byte ranges.");
        if (resp.Content.Headers.ContentRange is { From: long from } && from != pos)
            throw new InvalidDataException($"Server sent range starting at {from}, expected {pos}.");

        await using var stream = await resp.Content.ReadAsStreamAsync(stall.Token);
        while (true)
        {
            stall.CancelAfter(StallTimeout);
            int n = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), stall.Token);
            if (n == 0)
            {
                lock (job.Sync)
                    if (!seg.IsDone) throw new IOException("Connection closed before the segment completed.");
                return;
            }

            // Reserve the bytes under the lock so a concurrent split always sees the true position.
            long writeAt;
            int take;
            lock (job.Sync)
            {
                long remaining = seg.End - seg.Position + 1;
                if (remaining <= 0) return;
                take = (int)Math.Min(n, remaining);
                writeAt = seg.Position;
                seg.Downloaded += take;
            }

            try
            {
                await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, take), writeAt, ct);
            }
            catch
            {
                lock (job.Sync) seg.Downloaded -= take;
                throw;
            }

            if (take < n) return; // segment was shortened by a split
            lock (job.Sync)
                if (seg.IsDone) return;
        }
    }

    // ---------------------------------------------------------------- single connection fallback

    private async Task DownloadSingleStreamAsync(DownloadJob job, CancellationToken ct)
    {
        Segment seg;
        lock (job.Sync) seg = job.Segments[0];

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                lock (job.Sync) { seg.Downloaded = 0; seg.Active = true; }

                using var req = HttpFactory.CreateRequest(job.Url, job.Referrer);
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stall.CancelAfter(StallTimeout);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stall.Token);
                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}", null, resp.StatusCode);

                if (resp.Content.Headers.ContentLength is long len)
                {
                    job.TotalBytes = len;
                    lock (job.Sync) seg.End = len - 1;
                }

                await using var stream = await resp.Content.ReadAsStreamAsync(stall.Token);
                await using var fs = new FileStream(job.TempPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16, useAsync: true);
                var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                try
                {
                    while (true)
                    {
                        stall.CancelAfter(StallTimeout);
                        int n = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), stall.Token);
                        if (n == 0) break;
                        await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                        lock (job.Sync) seg.Downloaded += n;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                lock (job.Sync)
                {
                    if (seg.End != Segment.UnknownEnd && !seg.IsDone)
                        throw new IOException("Connection closed before the download completed.");
                    seg.End = seg.Downloaded - 1;
                    job.TotalBytes = seg.Downloaded;
                }
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < MaxRetries)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << attempt)), ct);
            }
            finally
            {
                lock (job.Sync) seg.Active = false;
            }
        }
    }

    // ---------------------------------------------------------------- helpers

    private static bool OtherWorkersActive(DownloadJob job, Segment self)
    {
        lock (job.Sync) return job.Segments.Any(s => s != self && s.Active);
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } sc } => (int)sc >= 500 || sc is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests,
        HttpRequestException => true,   // network error, DNS, connection reset
        IOException => true,
        OperationCanceledException => true, // stall timeout
        _ => false,
    };

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }

    private sealed class RetireWorkerException : Exception;

    private static class NativeMethods
    {
        private const uint FSCTL_SET_SPARSE = 0x000900C4;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize,
            IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        public static void TryMakeSparse(SafeFileHandle handle)
        {
            if (OperatingSystem.IsWindows())
                DeviceIoControl(handle, FSCTL_SET_SPARSE, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
        }
    }
}
