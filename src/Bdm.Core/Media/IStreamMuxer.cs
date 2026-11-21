using System.Diagnostics;

namespace Bdm.Core.Media;

/// <summary>
/// Joins a video-only and an audio-only stream into one playable file, without re-encoding.
/// YouTube serves HD video and audio separately, so this is what makes HD downloads possible.
/// Desktop: FFmpeg. Android: the OS's MediaMuxer (no FFmpeg needed). iOS: AVFoundation (future).
/// </summary>
public interface IStreamMuxer
{
    bool IsAvailable { get; }

    /// <summary>Whether this muxer accepts the codec (Android MediaMuxer: H.264/H.265 video + AAC audio).</summary>
    bool Supports(string videoCodec, string audioCodec);

    Task MergeAsync(string videoPath, string audioPath, string outputPath, CancellationToken ct);
}

/// <summary>FFmpeg stream copy (desktop, after the video tools are installed).</summary>
public sealed class FfmpegMuxer(Func<string?> ffmpegPath) : IStreamMuxer
{
    public bool IsAvailable => ffmpegPath() is not null;

    public bool Supports(string videoCodec, string audioCodec) => true;

    public async Task MergeAsync(string videoPath, string audioPath, string outputPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffmpegPath() ?? throw new InvalidOperationException("FFmpeg is not installed."))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
        };
        // The output may be a temp name (*.mp4.bdmpart), so name the container explicitly.
        var finalName = outputPath.EndsWith(".bdmpart", StringComparison.OrdinalIgnoreCase) ? outputPath[..^".bdmpart".Length] : outputPath;
        var format = Path.GetExtension(finalName).ToLowerInvariant() switch
        {
            ".mkv" => "matroska",
            ".webm" => "webm",
            _ => "mp4",
        };
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", videoPath, "-i", audioPath,
            "-map", "0:v:0", "-map", "1:a:0", "-c", "copy" };
        if (format == "mp4") args.AddRange(["-movflags", "+faststart"]);
        args.AddRange(["-f", format, outputPath]);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        ChildProcessJob.Add(p);
        var err = p.StandardError.ReadToEndAsync(ct);
        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch (InvalidOperationException) { }
            throw;
        }
        if (p.ExitCode != 0) throw new InvalidOperationException("Merging video and audio failed: " + (await err).Trim());
    }
}
