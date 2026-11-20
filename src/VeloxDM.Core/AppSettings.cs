using System.Text.Json;
using VeloxDM.Core.Media;

namespace VeloxDM.Core;

public sealed class AppSettings
{
    public string DownloadFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    /// <summary>Connections per download (also used as concurrent fragments for streams).</summary>
    public int Connections { get; set; } = 8;

    public int MaxConcurrentDownloads { get; set; } = 3;
    public bool MonitorClipboard { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool ShowCompletionNotification { get; set; } = true;
    public string DefaultMediaQuality { get; set; } = MediaQuality.Best;

    /// <summary>Use IPv4 for video sites (avoids stalls on networks with broken IPv6).</summary>
    public bool ForceIPv4ForVideo { get; set; } = true;

    /// <summary>
    /// Sign-in used for age-restricted / private videos: a cookies.txt file (written by VeloxDM's
    /// "Sign in to YouTube" window, or one you exported), or a browser name for --cookies-from-browser.
    /// </summary>
    public string? CookiesFile { get; set; }
    public string? CookiesFromBrowser { get; set; }

    public bool AutoUpdateYtDlp { get; set; } = true;
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
