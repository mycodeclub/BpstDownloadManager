namespace VeloxDM.Core.Media;

public sealed record MediaQualityOption(string Id, string Label);

/// <summary>Quality presets translated into yt-dlp format selectors.</summary>
public static class MediaQuality
{
    public const string Best = "best";

    public static IReadOnlyList<MediaQualityOption> Options { get; } =
    [
        new(Best, "Best available"),
        new("mp4", "Best MP4 (most compatible)"),
        new("2160", "Up to 4K (2160p)"),
        new("1440", "Up to 1440p"),
        new("1080", "Up to 1080p"),
        new("720", "Up to 720p"),
        new("480", "Up to 480p"),
        new("360", "Up to 360p"),
        new("audio-mp3", "Audio only (MP3)"),
        new("audio-m4a", "Audio only (M4A)"),
    ];

    /// <summary>
    /// Without FFmpeg yt-dlp cannot merge separate video and audio streams, so only
    /// single-file formats are requested (on YouTube this usually caps quality at 360p-720p).
    /// </summary>
    public static IReadOnlyList<string> BuildArgs(string? id, bool hasFfmpeg)
    {
        id ??= Best;
        if (int.TryParse(id, out int h))
            return hasFfmpeg ? ["-f", $"bv*[height<={h}]+ba/b[height<={h}]/b"] : ["-f", $"b[height<={h}]/b"];

        return id switch
        {
            "mp4" => hasFfmpeg
                ? ["-f", "bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/bv*+ba/b", "--merge-output-format", "mp4"]
                : ["-f", "b[ext=mp4]/b"],
            "audio-mp3" => hasFfmpeg
                ? ["-f", "ba/b", "-x", "--audio-format", "mp3", "--audio-quality", "0"]
                : ["-f", "ba[ext=m4a]/ba/b"],
            "audio-m4a" => hasFfmpeg
                ? ["-f", "ba[ext=m4a]/ba/b", "-x", "--audio-format", "m4a"]
                : ["-f", "ba[ext=m4a]/ba/b"],
            _ => hasFfmpeg ? ["-f", "bv*+ba/b"] : ["-f", "b"],
        };
    }
}
