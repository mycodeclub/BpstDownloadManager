namespace Bdm.Core.Finder;

/// <summary>
/// Keeps Smart Finder to genuine sources: results on known piracy sites (pirate torrent indexes, pirate movie and TV
/// sites, cracked-game and warez sites, shadow libraries, leaked-course sites) are never shown.
/// </summary>
public static class SourceGuard
{
    private static readonly string[] BlockedHosts =
    [
        // Torrent indexes known for unlicensed content
        "thepiratebay.org", "piratebay", "1337x.to", "1337x", "rarbg", "yts.mx", "yts.am", "yts.lt", "yts",
        "limetorrents", "torrentgalaxy", "kickass", "katcr", "eztv", "torlock", "torrentz", "zooqle",
        "magnetdl", "bitsearch", "btdig", "solidtorrents", "glodls", "idope", "nyaa.si",
        "ytsrs", "yts-official", "ytstv", "torrentquest", "torrentdownloads", "torrentfunk", "1377x", "x1337x",
        // Movie and TV piracy (streaming and direct-download sites, many targeting India)
        "tamilrockers", "movierulz", "filmyzilla", "filmywap", "filmyhit", "9xmovies", "vegamovies", "hdhub4u", "katmoviehd",
        "bolly4u", "moviesflix", "mkvcinemas", "ssrmovies", "worldfree4u", "khatrimaza", "extramovies", "isaimini", "tamilyogi",
        "tamilblasters", "moviesda", "jalshamoviez", "skymovieshd", "mp4moviez", "filmy4wap", "moviesverse", "uwatchfree",
        "fmovies", "123movies", "gomovies", "putlocker", "soap2day", "sflix", "lookmovie", "hdtoday", "flixtor", "myflixer", "hurawatch",
        // Cracked games and warez
        "torgamez", "fitgirl-repacks", "dodi-repacks", "igg-games", "steamunlocked", "oceanofgames", "getintopc",
        "skidrowreloaded", "skidrow-games", "crackwatch", "repack-games", "gog-games", "ova-games",
        "filecr", "softonic-crack", "apkpure-mod", "an1.com", "happymod", "moddroid",
        // Shadow libraries
        "libgen", "library.lol", "z-lib", "zlibrary", "z-library", "singlelogin", "1lib", "annas-archive",
        "sci-hub", "pdfdrive", "ebook3000", "dokumen.pub",
        // Leaked paid courses
        "freecoursesite", "courseclub", "tutsnode", "downloadly", "freetutorials", "paidcoursesforfree",
        "freecourseweb", "courselala", "tutflix", "coursehunter", "gfxdownload", "desirecourse",
    ];

    private static readonly string[] PiracyWords =
    [
        "crack", "cracked", "keygen", "repack", "leaked", "leak download", "nulled", "warez", "mod apk", "modded apk", "pirated",
        "free download", "download free", "full version", "full game free", "torrent download", "activation key", "serial key",
        "skidrow", "fitgirl", "unlocked game",
    ];

    /// <summary>Whether a result's title advertises piracy ("crack", "leak", "full version free download"…).</summary>
    public static bool LooksPirated(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var letters = new string(title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
        var t = " " + string.Join(' ', letters.Split(' ', StringSplitOptions.RemoveEmptyEntries)) + " ";
        return PiracyWords.Any(w => t.Contains(" " + w + " ", StringComparison.Ordinal));
    }

    /// <summary>Whether a link points to a known piracy site.</summary>
    public static bool IsBlocked(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.ToLowerInvariant();
        var labels = host.Split('.');
        return BlockedHosts.Any(b => b.Contains('.')
            ? host == b || host.EndsWith("." + b, StringComparison.Ordinal)
            : labels.Any(label => IsVariant(label, b)));
    }

    // "1337x", "1337x2", "1337x-proxy" all match "1337x"; "ytsomething" doesn't match "yts".
    private static bool IsVariant(string label, string name) =>
        label == name ||
        label.StartsWith(name + "-", StringComparison.Ordinal) ||
        label.StartsWith(name, StringComparison.Ordinal) && label[name.Length..].All(char.IsDigit);
}
