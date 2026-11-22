namespace Bdm.Core;

public enum UrlKind
{
    Invalid,
    /// <summary>blob: URL — only exists inside the browser tab that created it.</summary>
    Blob,
    /// <summary>Looks like a direct file (by extension).</summary>
    Direct,
    /// <summary>Video site page or DASH manifest — yt-dlp / built-in YouTube downloader.</summary>
    Media,
    /// <summary>http(s) URL of unknown type — needs probing.</summary>
    Unknown,
    /// <summary>HLS playlist (.m3u8) — built-in stream downloader.</summary>
    Hls,
    /// <summary>magnet: link or .torrent file URL.</summary>
    Torrent,
    /// <summary>Subscription streaming service whose videos are DRM-encrypted (Netflix, Prime Video...). Not downloadable.</summary>
    Protected,
}

public static class UrlClassifier
{
    private static readonly string[] MediaHosts =
    [
        "youtube.com", "youtu.be", "youtube-nocookie.com", "vimeo.com", "dailymotion.com", "twitch.tv",
        "x.com", "twitter.com", "facebook.com", "fb.watch", "instagram.com", "tiktok.com", "reddit.com",
        "v.redd.it", "soundcloud.com", "bilibili.com", "streamable.com", "bandcamp.com", "rumble.com",
        "ted.com", "archive.org", "odysee.com", "kick.com",
    ];

    /// <summary>
    /// Subscription (OTT) services that encrypt their videos with DRM (Widevine/PlayReady/FairPlay).
    /// BDM does not and will not circumvent DRM; it says so up front instead of failing later.
    /// </summary>
    private static readonly (string Host, string Name)[] DrmServices =
    [
        ("netflix.com", "Netflix"), ("primevideo.com", "Prime Video"), ("disneyplus.com", "Disney+"),
        ("hotstar.com", "JioHotstar"), ("jiohotstar.com", "JioHotstar"), ("jiocinema.com", "JioCinema"),
        ("hulu.com", "Hulu"), ("max.com", "Max"), ("hbomax.com", "Max"), ("tv.apple.com", "Apple TV+"),
        ("peacocktv.com", "Peacock"), ("paramountplus.com", "Paramount+"), ("sonyliv.com", "SonyLIV"),
        ("zee5.com", "ZEE5"), ("crunchyroll.com", "Crunchyroll"), ("open.spotify.com", "Spotify"),
        ("music.apple.com", "Apple Music"), ("aha.video", "aha"), ("sunnxt.com", "Sun NXT"),
    ];

    /// <summary>Name of the DRM streaming service this link belongs to, or null.</summary>
    public static string? ProtectedServiceName(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant();
        foreach (var (h, name) in DrmServices)
            if (host == h || host.EndsWith("." + h, StringComparison.Ordinal)) return name;
        // Amazon's own domains host Prime Video under /gp/video.
        if (host.Contains("amazon.") && uri.AbsolutePath.Contains("/gp/video", StringComparison.OrdinalIgnoreCase)) return "Prime Video";
        return null;
    }

    public static string ProtectedMessage(string service) =>
        $"{service} protects its videos with DRM (copy protection), so BDM can't download them, and circumventing it would break copyright law. " +
        $"Use the offline download feature in the official {service} app instead.";

    private static readonly HashSet<string> StreamExtensions = new(StringComparer.OrdinalIgnoreCase) { ".m3u8", ".mpd", ".f4m", ".ism" };

    private static readonly Dictionary<string, string> Categories = BuildCategories();

    public static UrlKind Classify(string? text) => Classify(text, out _);

    public static UrlKind Classify(string? text, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(text)) return UrlKind.Invalid;
        text = text.Trim();
        if (text.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) return UrlKind.Blob;
        if (text.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) && text.Contains("xt=urn:bt", StringComparison.OrdinalIgnoreCase))
            return UrlKind.Torrent;
        if (!Uri.TryCreate(text, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            uri = null;
            return UrlKind.Invalid;
        }

        if (ProtectedServiceName(text) is not null) return UrlKind.Protected;
        var ext = Path.GetExtension(uri.AbsolutePath);
        if (ext.Equals(".torrent", StringComparison.OrdinalIgnoreCase)) return UrlKind.Torrent;
        if (ext.Equals(".m3u8", StringComparison.OrdinalIgnoreCase)) return UrlKind.Hls;
        if (StreamExtensions.Contains(ext)) return UrlKind.Media;

        var host = uri.Host.ToLowerInvariant();
        if (MediaHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal)))
        {
            // Direct files hosted on those domains (e.g. archive.org/download/x.zip) still go to the HTTP engine.
            return Categories.ContainsKey(ext) && !host.Contains("youtu") ? UrlKind.Direct : UrlKind.Media;
        }

        return Categories.ContainsKey(ext) ? UrlKind.Direct : UrlKind.Unknown;
    }

    /// <summary>Extracts a single URL from clipboard text (ignores multi-word text).</summary>
    public static string? ExtractUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().Trim('"', '\'', '<', '>');
        if (text.Length > 4096 || text.Any(char.IsWhiteSpace)) return null;
        return Classify(text) == UrlKind.Invalid ? null : text;
    }

    /// <summary>Whether clipboard monitoring should react to this URL.</summary>
    public static bool IsCatchable(string url) =>
        Classify(url) is UrlKind.Direct or UrlKind.Media or UrlKind.Blob or UrlKind.Hls or UrlKind.Torrent;

    public static string CategoryOf(string fileName) =>
        Categories.TryGetValue(Path.GetExtension(fileName), out var c) ? c : "Other";

    public static IReadOnlyList<string> AllCategories { get; } = ["Video", "Music", "Documents", "Compressed", "Programs", "Images", "Other"];

    private static Dictionary<string, string> BuildCategories()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string cat, string exts) { foreach (var e in exts.Split(' ')) map["." + e] = cat; }
        Add("Video", "mp4 mkv webm avi mov wmv flv m4v mpg mpeg 3gp ts m2ts vob ogv");
        Add("Music", "mp3 m4a aac flac wav ogg opus wma aiff alac mka");
        Add("Documents", "pdf doc docx xls xlsx ppt pptx odt ods odp txt rtf epub mobi csv");
        Add("Compressed", "zip rar 7z tar gz tgz bz2 xz zst cab");
        Add("Programs", "exe msi msix msixbundle appx apk dmg pkg deb rpm iso img bin jar");
        Add("Images", "jpg jpeg png gif webp bmp tiff svg heic avif");
        return map;
    }
}
