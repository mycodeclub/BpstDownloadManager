using System.Text.Json;
using Bdm.Core.Media;

namespace Bdm.Core;

public sealed class AppSettings
{
    /// <summary>Where files are saved. Empty = platform default (set by the app at startup).</summary>
    public string DownloadFolder { get; set; } = "";

    /// <summary>Connections per download (also used as concurrent fragments for streams).</summary>
    public int Connections { get; set; } = 8;

    public int MaxConcurrentDownloads { get; set; } = 3;

    // ---- bandwidth
    /// <summary>Overall download speed limit in KB/s (0 = unlimited).</summary>
    public int GlobalSpeedLimitKBps { get; set; }

    /// <summary>
    /// While one download has Focus, other HTTP/HLS/torrent downloads are slowed to this speed (KB/s) so they keep
    /// their connections. 0 = pause them instead.
    /// </summary>
    public int FocusOthersKBps { get; set; } = 16;

    // ---- long downloads
    /// <summary>After a network failure, retry the whole download automatically this many times (0 = off).</summary>
    public int AutoRetryCount { get; set; } = 20;

    /// <summary>Ask the OS not to sleep while downloads are running.</summary>
    public bool KeepAwake { get; set; } = true;

    // ---- torrent
    public int TorrentPort { get; set; } = 55125;
    /// <summary>Keep sharing finished torrents until this upload/download ratio (0 = stop when done).</summary>
    public double TorrentSeedRatio { get; set; }
    public int TorrentUploadLimitKBps { get; set; }
    public bool TorrentDht { get; set; } = true;
    public bool TorrentPortForwarding { get; set; } = true;
    /// <summary>Accept incoming peer connections (faster; Windows asks once to allow it through the firewall).</summary>
    public bool TorrentIncoming { get; set; } = true;

    // ---- desktop behaviour
    public bool MonitorClipboard { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool ShowCompletionNotification { get; set; } = true;

    // ---- video
    public string DefaultMediaQuality { get; set; } = MediaQuality.Best;

    /// <summary>Use IPv4 for video sites (avoids stalls on networks with broken IPv6).</summary>
    public bool ForceIPv4ForVideo { get; set; } = true;

    /// <summary>
    /// Sign-in used for age-restricted / private videos: a cookies.txt file (written by the "Sign in to YouTube"
    /// page, or one you exported), or a browser name for yt-dlp's --cookies-from-browser.
    /// </summary>
    public string? CookiesFile { get; set; }
    public string? CookiesFromBrowser { get; set; }

    public bool AutoUpdateYtDlp { get; set; } = true;

    /// <summary>Desktop: install yt-dlp/FFmpeg automatically the first time a video is downloaded.</summary>
    public bool AutoInstallVideoTools { get; set; } = true;
    public DateTime LastYtDlpUpdateCheckUtc { get; set; }

    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}

internal static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
