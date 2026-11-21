using System.IO.Compression;
using Bdm.Core.Http;

namespace Bdm.Core.Media;

/// <summary>
/// Locates and installs the external helpers used for video sites and streams.
/// They are downloaded from their official GitHub releases into the app's tools folder
/// and run as separate processes (BpstVideoDownloader does not bundle or link them).
/// </summary>
public sealed class MediaTools
{
    public const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    public const string FfmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
    public const string DenoUrl = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";

    public MediaTools(string toolsDir) => ToolsDir = toolsDir;

    public string ToolsDir { get; }

    /// <summary>yt-dlp/FFmpeg/Deno are Windows programs here; mobile uses the built-in backends instead.</summary>
    public bool IsSupported => OperatingSystem.IsWindows();

    public string? YtDlpPath => Find("yt-dlp.exe");
    public string? FfmpegPath => Find("ffmpeg.exe");

    /// <summary>yt-dlp needs a JavaScript runtime for YouTube; Deno is the recommended one.</summary>
    public string? JsRuntimePath => DenoPath ?? NodePath;
    public string? DenoPath => Find("deno.exe");
    public string? NodePath => Find("node.exe");

    /// <summary>Runs "yt-dlp -U" (self-update of the official release build). Returns its output.</summary>
    public async Task<string> UpdateYtDlpAsync(CancellationToken ct)
    {
        var exe = YtDlpPath ?? throw new InvalidOperationException("yt-dlp is not installed.");
        var psi = new System.Diagnostics.ProcessStartInfo(exe, "-U")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        ChildProcessJob.Add(p);
        var output = await p.StandardOutput.ReadToEndAsync(ct) + await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return output.Trim();
    }

    private string? Find(string exe)
    {
        var local = Path.Combine(ToolsDir, exe);
        if (File.Exists(local)) return local;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    public Task InstallYtDlpAsync(HttpClient http, IProgress<string>? status, CancellationToken ct) =>
        DownloadFileAsync(http, YtDlpUrl, Path.Combine(ToolsDir, "yt-dlp.exe"), "yt-dlp", status, ct);

    public async Task InstallFfmpegAsync(HttpClient http, IProgress<string>? status, CancellationToken ct)
    {
        var zip = Path.Combine(ToolsDir, "ffmpeg.zip");
        await DownloadFileAsync(http, FfmpegUrl, zip, "FFmpeg", status, ct);
        status?.Report("Extracting FFmpeg...");
        await Task.Run(() => ExtractExes(zip, "ffmpeg.exe", "ffprobe.exe"), ct);
        File.Delete(zip);
        status?.Report("FFmpeg installed.");
    }

    public async Task InstallDenoAsync(HttpClient http, IProgress<string>? status, CancellationToken ct)
    {
        var zip = Path.Combine(ToolsDir, "deno.zip");
        await DownloadFileAsync(http, DenoUrl, zip, "Deno", status, ct);
        status?.Report("Extracting Deno...");
        await Task.Run(() => ExtractExes(zip, "deno.exe"), ct);
        File.Delete(zip);
        status?.Report("Deno installed.");
    }

    private void ExtractExes(string zipPath, params string[] names)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var name in names)
        {
            var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException($"{name} not found in archive.");
            entry.ExtractToFile(Path.Combine(ToolsDir, name), overwrite: true);
        }
    }

    private async Task DownloadFileAsync(HttpClient http, string url, string target, string label, IProgress<string>? status, CancellationToken ct)
    {
        Directory.CreateDirectory(ToolsDir);
        var temp = target + ".download";
        using var req = HttpFactory.CreateRequest(url, null);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;

        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(temp))
        {
            var buffer = new byte[81920];
            long done = 0;
            var lastReport = DateTime.MinValue;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;
                if (DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(250))
                {
                    lastReport = DateTime.UtcNow;
                    status?.Report(total is > 0
                        ? $"Downloading {label}... {done * 100 / total.Value}% ({Format.Bytes(done)} of {Format.Bytes(total.Value)})"
                        : $"Downloading {label}... {Format.Bytes(done)}");
                }
            }
        }
        File.Move(temp, target, overwrite: true);
        status?.Report($"{label} installed.");
    }
}
