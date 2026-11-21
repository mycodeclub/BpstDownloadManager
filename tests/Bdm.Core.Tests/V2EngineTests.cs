using System.Diagnostics;
using Bdm.Core.Hls;
using Bdm.Core.Http;
using Bdm.Core.Torrent;

namespace Bdm.Core.Tests;

public sealed class V2EngineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bdm-v2-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = HttpFactory.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ------------------------------------------------------------- rate limiter / focus

    [Fact]
    public async Task Rate_limiter_holds_the_average_speed()
    {
        var limiter = new RateLimiter { BytesPerSecond = 200_000 };
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 40; i++) await limiter.ConsumeAsync(16_384, CancellationToken.None); // ~655 KB
        // 1 s burst allowance + ~2.3 s of throttled transfer
        Assert.InRange(sw.Elapsed.TotalSeconds, 1.8, 6);
    }

    [Fact]
    public async Task Unlimited_rate_limiter_never_waits()
    {
        var limiter = new RateLimiter();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) await limiter.ConsumeAsync(1 << 20, CancellationToken.None);
        Assert.True(sw.ElapsedMilliseconds < 500);
    }

    [Fact]
    public async Task Limited_segmented_download_is_slower_but_complete()
    {
        await using var server = await TestServer.StartAsync(600_000, chunkDelayMs: 0);
        var job = new DownloadJob { Url = server.BaseUrl + "/ranged", Folder = _dir };
        var limiter = new RateLimiter { BytesPerSecond = 300_000 };
        var sw = Stopwatch.StartNew();
        await new SegmentedDownloader(_http) { Connections = 4, MinSegmentSize = 64 * 1024, Limiters = [limiter] }
            .DownloadAsync(job, CancellationToken.None);
        Assert.True(sw.Elapsed.TotalSeconds >= 0.9, $"took {sw.Elapsed.TotalSeconds:0.00}s");
        Assert.Equal(server.Payload, await File.ReadAllBytesAsync(job.FullPath));
    }

    [Fact]
    public async Task Focus_throttles_the_other_downloads_and_restores_them()
    {
        await using var server = await TestServer.StartAsync(8 * 1024 * 1024, chunkDelayMs: 20);
        using var m = new DownloadManager(Path.Combine(_dir, "data"), _dir);
        m.Settings.FocusOthersKBps = 16;
        m.Settings.MaxConcurrentDownloads = 3;
        var a = m.Add(new DownloadJob { Url = server.BaseUrl + "/ranged", FileName = "a.bin" }, startNow: true);
        var b = m.Add(new DownloadJob { Url = server.BaseUrl + "/ranged", FileName = "b.bin" }, startNow: true);
        while (!a.IsRunning || !b.IsRunning) await Task.Delay(10);

        m.SetFocus(a);
        Assert.Equal(a.Id, m.FocusedJobId);
        Assert.Equal(16 * 1024, b.Limiter.BytesPerSecond); // slowed, connection kept
        Assert.Equal(0, a.Limiter.BytesPerSecond);         // full speed
        Assert.True(b.IsRunning);

        m.SetFocus(null);
        Assert.Equal(0, b.Limiter.BytesPerSecond);
        m.PauseAll();
    }

    // ------------------------------------------------------------- changed file on the server

    [Fact]
    public async Task Resume_detects_a_changed_file_instead_of_mixing_versions()
    {
        await using var server = await TestServer.StartAsync(2 * 1024 * 1024, chunkDelayMs: 5);
        var job = new DownloadJob { Url = server.BaseUrl + "/ranged", Folder = _dir };
        var dl = new SegmentedDownloader(_http) { Connections = 4, MinSegmentSize = 64 * 1024 };

        using (var cts = new CancellationTokenSource())
        {
            var run = dl.DownloadAsync(job, cts.Token);
            while (job.DownloadedBytes < 256 * 1024) await Task.Delay(10);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        }
        Assert.Equal("\"v1\"", job.ETag);

        server.Version = 2; // the publisher replaced the file
        await Assert.ThrowsAsync<RemoteFileChangedException>(() => dl.DownloadAsync(job, CancellationToken.None));
    }

    // ------------------------------------------------------------- HLS

    [Fact]
    public void Parses_master_and_media_playlists()
    {
        var master = HlsPlaylist.Parse("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360,CODECS=\"avc1.4d401e,mp4a.40.2\"\nlow/index.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080\nhi/index.m3u8\n",
            new Uri("https://cdn.example.com/v/master.m3u8"));
        Assert.True(master.IsMaster);
        Assert.Equal("https://cdn.example.com/v/hi/index.m3u8", master.PickVariant(null).Uri.ToString());
        Assert.Equal(360, master.PickVariant(720).Height);

        var media = HlsPlaylist.Parse("#EXTM3U\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXT-X-KEY:METHOD=AES-128,URI=\"https://k.example.com/key\",IV=0x000102030405060708090A0B0C0D0E0F\n#EXTINF:6.0,\na.m4s\n#EXTINF:6.0,\nb.m4s\n#EXT-X-ENDLIST",
            new Uri("https://cdn.example.com/v/hi/index.m3u8"));
        Assert.False(media.IsEndless);
        Assert.Equal(2, media.Segments.Count);
        Assert.Equal("https://cdn.example.com/v/hi/init.mp4", media.InitSegment!.ToString());
        Assert.Equal(16, media.Segments[0].Key!.Iv!.Length);
        Assert.Equal(15, media.Segments[0].Key!.Iv![15]);
    }

    [Fact]
    public async Task Downloads_encrypted_hls_stream_and_joins_parts_in_order()
    {
        await using var server = await TestServer.StartAsync(1024, chunkDelayMs: 1);
        var job = new DownloadJob { Url = server.BaseUrl + "/hls/master.m3u8", Folder = _dir, Kind = DownloadKind.Hls, OutputName = "My stream" };

        await new HlsDownloader(_http) { Connections = 4 }.DownloadAsync(job, maxHeight: null, CancellationToken.None);

        Assert.Equal("My stream.ts", job.FileName);
        var expected = TestServer.HlsSegments.SelectMany(s => s).ToArray();
        Assert.Equal(expected, await File.ReadAllBytesAsync(job.FullPath));
        Assert.False(Directory.Exists(job.FullPath + ".bdmparts"));
    }

    [Fact]
    public async Task Hls_resumes_only_missing_segments()
    {
        await using var server = await TestServer.StartAsync(1024, chunkDelayMs: 20);
        var job = new DownloadJob { Url = server.BaseUrl + "/hls/media.m3u8", Folder = _dir, Kind = DownloadKind.Hls };
        var dl = new HlsDownloader(_http) { Connections = 2 };

        using (var cts = new CancellationTokenSource())
        {
            var run = dl.DownloadAsync(job, null, cts.Token);
            while ((job.HlsDone?.Count ?? 0) < 3) await Task.Delay(10);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        }
        int doneBefore = job.HlsDone!.Count;
        Assert.InRange(doneBefore, 3, TestServer.HlsSegments.Length - 1);

        await dl.DownloadAsync(job, null, CancellationToken.None);
        Assert.Equal(TestServer.HlsSegments.SelectMany(s => s).ToArray(), await File.ReadAllBytesAsync(job.FullPath));
    }

    [Fact]
    public void Sequence_iv_is_big_endian()
    {
        var iv = HlsDownloader.SequenceIv(0x0102);
        Assert.Equal(0x01, iv[14]);
        Assert.Equal(0x02, iv[15]);
        Assert.All(iv.Take(14), b => Assert.Equal(0, b));
    }

    // ------------------------------------------------------------- torrent

    [Theory]
    [InlineData("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=ubuntu.iso", UrlKind.Torrent)]
    [InlineData("https://releases.ubuntu.com/24.04/ubuntu-24.04-desktop-amd64.iso.torrent", UrlKind.Torrent)]
    [InlineData("https://cdn.example.com/live/master.m3u8?token=1", UrlKind.Hls)]
    [InlineData("magnet:?dn=no-hash", UrlKind.Invalid)]
    public void Classifies_v2_links(string url, UrlKind kind) => Assert.Equal(kind, UrlClassifier.Classify(url));

    [Fact]
    public async Task Reads_a_torrent_file()
    {
        // Build a real .torrent for a small file with MonoTorrent's creator.
        Directory.CreateDirectory(_dir);
        var content = Path.Combine(_dir, "sample.bin");
        await File.WriteAllBytesAsync(content, new byte[300_000]);
        var creator = new MonoTorrent.TorrentCreator();
        var dict = await creator.CreateAsync(new MonoTorrent.TorrentFileSource(content));
        var torrentPath = Path.Combine(_dir, "sample.torrent");
        await File.WriteAllBytesAsync(torrentPath, dict.Encode());

        var meta = await TorrentService.ReadAsync(torrentPath);
        Assert.Equal("sample.bin", meta.Name);
        Assert.Equal(300_000, meta.Size);
        Assert.Single(meta.Files);
    }
}
