using VeloxDM.Core.Http;

namespace VeloxDM.Core.Tests;

public sealed class SegmentedDownloaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vdm-tests-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = HttpFactory.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public async Task Downloads_with_multiple_connections_and_matches_bytes()
    {
        await using var server = await TestServer.StartAsync(4 * 1024 * 1024);
        var job = new DownloadJob { Url = server.BaseUrl + "/ranged", Folder = _dir };
        var dl = new SegmentedDownloader(_http) { Connections = 8, MinSegmentSize = 64 * 1024 };

        await dl.DownloadAsync(job, CancellationToken.None);

        Assert.Equal("payload.bin", job.FileName);
        Assert.True(job.SupportsRanges);
        Assert.Equal(server.Payload, await File.ReadAllBytesAsync(job.FullPath));
        Assert.False(File.Exists(job.TempPath));
        Assert.True(server.MaxConcurrent >= 4, $"expected parallel connections, saw {server.MaxConcurrent}");
        Assert.True(job.Segments.Count >= 8, "dynamic splitting should create extra segments");
    }

    [Fact]
    public async Task Falls_back_to_single_stream_without_range_support()
    {
        await using var server = await TestServer.StartAsync(1024 * 1024, chunkDelayMs: 0);
        var job = new DownloadJob { Url = server.BaseUrl + "/plain", Folder = _dir };

        await new SegmentedDownloader(_http) { Connections = 8 }.DownloadAsync(job, CancellationToken.None);

        Assert.False(job.SupportsRanges);
        Assert.Equal(server.Payload, await File.ReadAllBytesAsync(job.FullPath));
    }

    [Fact]
    public async Task Pause_then_resume_produces_identical_file()
    {
        await using var server = await TestServer.StartAsync(4 * 1024 * 1024, chunkDelayMs: 5);
        var job = new DownloadJob { Url = server.BaseUrl + "/ranged", Folder = _dir };
        var dl = new SegmentedDownloader(_http) { Connections = 4, MinSegmentSize = 64 * 1024 };

        using (var cts = new CancellationTokenSource())
        {
            var run = dl.DownloadAsync(job, cts.Token);
            while (job.DownloadedBytes < 512 * 1024) await Task.Delay(10);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        }

        long partial = job.DownloadedBytes;
        Assert.InRange(partial, 1, server.Payload.Length - 1);
        Assert.True(File.Exists(job.TempPath));

        // Simulate an app restart: round-trip the job through persistence.
        var restored = System.Text.Json.JsonSerializer.Deserialize<DownloadJob>(System.Text.Json.JsonSerializer.Serialize(job.CloneForSave()))!;
        Assert.Equal(partial, restored.DownloadedBytes);

        await dl.DownloadAsync(restored, CancellationToken.None);
        Assert.Equal(server.Payload, await File.ReadAllBytesAsync(restored.FullPath));
    }
}
