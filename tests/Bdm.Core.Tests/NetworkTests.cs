using System.Text.RegularExpressions;
using Bdm.Core.Http;
using Bdm.Core.Torrent;

namespace Bdm.Core.Tests;

/// <summary>
/// Real-internet checks, opt-in: set BDM_NET_TESTS=1. They talk to public servers/peers, so they're not part of
/// the normal offline test run.
/// </summary>
public sealed class NetworkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bdm-net-" + Guid.NewGuid().ToString("N"));
    private static bool Enabled => Environment.GetEnvironmentVariable("BDM_NET_TESTS") == "1";

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static string? FindTool(string exe) =>
        new[] { "BDM", "BpstVideoDownloader" }
            .Select(d => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), d, "tools", exe))
            .FirstOrDefault(File.Exists);

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=hJuOv-y6SLQ&list=RDhJuOv-y6SLQ&start_radio=1", "360")]
    public async Task YouTube_built_in_downloads_separate_streams_and_merges(string url, string quality)
    {
        if (!Enabled) return;
        var ffmpeg = FindTool("ffmpeg.exe");
        Assert.NotNull(ffmpeg);
        Directory.CreateDirectory(_dir);
        using var http = HttpFactory.CreateClient();
        var backend = new Media.YoutubeExplodeBackend(http, () => null, () => new Media.FfmpegMuxer(() => ffmpeg));

        var job = new DownloadJob { Url = url, Kind = DownloadKind.Media, Folder = _dir, MediaQuality = quality };
        await backend.DownloadAsync(job, 8, [], CancellationToken.None);

        Assert.True(File.Exists(job.FullPath), job.FullPath);
        Assert.EndsWith(".mp4", job.FileName);
        Assert.True(new FileInfo(job.FullPath).Length > 1_000_000);
        Assert.Null(job.Parts); // temp streams cleaned up
        Assert.Empty(Directory.GetFiles(_dir, "*.bdmtmp*"));

        // The merged file must contain both a video and an audio stream.
        var psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"),
            $"-v error -show_entries stream=codec_type -of csv=p=0 \"{job.FullPath}\"") { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var streams = (await p.StandardOutput.ReadToEndAsync()).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("video", streams);
        Assert.Contains("audio", streams);
    }

    [Fact]
    public async Task YouTube_through_the_manager_with_ytdlp_tools()
    {
        if (!Enabled) return;
        var ytdlp = FindTool("yt-dlp.exe");
        var ffmpeg = FindTool("ffmpeg.exe");
        Assert.NotNull(ytdlp);
        // Same layout as after BDM's automatic one-time setup: <data>/tools/{yt-dlp,ffmpeg,ffprobe}.exe
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(Path.Combine(data, "tools"));
        foreach (var exe in new[] { ytdlp!, ffmpeg!, Path.Combine(Path.GetDirectoryName(ffmpeg!)!, "ffprobe.exe") })
            File.Copy(exe, Path.Combine(data, "tools", Path.GetFileName(exe)));

        using var m = new DownloadManager(data, Path.Combine(_dir, "files"));
        m.Settings.AutoUpdateYtDlp = false;
        var job = m.Add(new DownloadJob { Url = "https://www.youtube.com/watch?v=hJuOv-y6SLQ&list=RDhJuOv-y6SLQ&start_radio=1", Kind = DownloadKind.Media, MediaQuality = "360" }, startNow: true);

        var deadline = DateTime.UtcNow.AddMinutes(12);
        while (job.Status is not (DownloadStatus.Completed or DownloadStatus.Failed) && DateTime.UtcNow < deadline) await Task.Delay(1000);
        Assert.True(job.Status == DownloadStatus.Completed, $"{job.Status}: {job.Error}");
        Assert.True(File.Exists(job.FullPath), job.FullPath);
        Assert.True(new FileInfo(job.FullPath).Length > 1_000_000);
    }

    [Fact]
    public async Task YouTube_without_muxer_reports_a_clear_problem_or_uses_a_combined_stream()
    {
        if (!Enabled) return;
        using var http = HttpFactory.CreateClient();
        var client = new YoutubeExplode.YoutubeClient(http);
        var manifest = await client.Videos.Streams.GetManifestAsync("hJuOv-y6SLQ");
        Assert.NotEmpty(manifest.GetVideoOnlyStreams());
        Assert.NotEmpty(manifest.GetAudioOnlyStreams());
        try
        {
            var plan = Media.YoutubeExplodeBackend.Choose(manifest, "best", muxer: null);
            Assert.NotNull(plan.Single);
        }
        catch (InvalidOperationException ex)
        {
            Assert.Contains("separate video and audio", ex.Message);
        }
    }

    [Fact]
    public async Task Torrent_downloads_from_real_peers()
    {
        if (!Enabled) return;
        Directory.CreateDirectory(_dir);
        using var http = HttpFactory.CreateClient();

        // Debian's current netinst .torrent (always served by Debian's tracker; the name changes with point releases).
        const string baseUrl = "https://cdimage.debian.org/debian-cd/current/amd64/bt-cd/";
        var index = await http.GetStringAsync(baseUrl);
        var name = Regex.Match(index, "href=\"(debian-[0-9.]+-amd64-netinst\\.iso\\.torrent)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(name), "No .torrent link found on cdimage.debian.org");
        var torrentPath = Path.Combine(_dir, name);
        await File.WriteAllBytesAsync(torrentPath, await http.GetByteArrayAsync(baseUrl + name));

        var settings = new AppSettings { TorrentIncoming = false, TorrentDht = false, TorrentPortForwarding = false };
        await using var service = new TorrentService(Path.Combine(_dir, "data"), settings);
        var job = new DownloadJob { Kind = DownloadKind.Torrent, Url = "file:" + name, Folder = Path.Combine(_dir, "files") };
        job.TorrentFile = service.Import(torrentPath, job.Id);

        using var cts = new CancellationTokenSource();
        var run = service.DownloadAsync(job, 0, cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (job.MediaDownloaded < 3 * 1024 * 1024 && DateTime.UtcNow < deadline && !run.IsCompleted) await Task.Delay(1000);
        long got = job.MediaDownloaded;
        string detail = service.Describe(job.Id);
        cts.Cancel();
        try { await run; } catch (OperationCanceledException) { }
        await service.StopAndRemoveAsync(job.Id, deleteData: true);

        Assert.True(got > 0, $"No data received from peers in 120 s ({detail}).");
        Assert.StartsWith("debian-", job.FileName);
        Assert.True(job.TotalBytes > 300_000_000, "Expected the ISO size from the torrent metadata.");
    }
}
