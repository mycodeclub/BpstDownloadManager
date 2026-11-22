using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeExplode;
using YoutubeExplode.Common;

namespace Bdm.Core.Finder;

/// <summary>"Search for it on…" links to official stores and free learning platforms (no API keys needed).</summary>
public sealed class StoreLinksProvider : IFinderProvider
{
    public string Name => "Official stores";

    public Task<IReadOnlyList<FinderResult>> SearchAsync(FinderQuery q, CancellationToken ct)
    {
        var e = Uri.EscapeDataString(q.Terms);
        var amazon = q.Region == "IN" ? "amazon.in" : "amazon.com";
        var list = new List<FinderResult>();
        void Buy(FinderKind kind, string source, string url, string detail) =>
            list.Add(new(FinderSection.BuyOrRent, kind, $"Search \"{q.Terms}\" on {source}", url, source) { Detail = detail, IsSearchLink = true });
        void Free(FinderKind kind, string source, string url, string detail) =>
            list.Add(new(FinderSection.FreeLegal, kind, $"Search \"{q.Terms}\" on {source}", url, source) { Detail = detail, IsSearchLink = true, License = "Free platform" });

        bool any = q.Intent == FinderIntent.Any;
        if (any || q.Intent == FinderIntent.Game)
        {
            Buy(FinderKind.Game, "Epic Games Store", $"https://store.epicgames.com/en-US/browse?q={e}", "PC games");
            Buy(FinderKind.Game, "PlayStation Store", $"https://store.playstation.com/en-{(q.Region == "IN" ? "in" : "us")}/search/{e}", "PS5 / PS4");
            Buy(FinderKind.Game, "Xbox Store", $"https://www.xbox.com/en-US/Search/Results?q={e}", "Xbox and PC Game Pass");
            Free(FinderKind.Game, "Epic free games", "https://store.epicgames.com/en-US/free-games", "Free games every week, yours to keep");
        }
        if (any || q.Intent == FinderIntent.Course)
        {
            Buy(FinderKind.Course, "Udemy", $"https://www.udemy.com/courses/search/?q={e}", "Paid courses, frequent sales");
            Buy(FinderKind.Course, "Coursera", $"https://www.coursera.org/search?query={e}", "Many courses can be audited for free");
            Free(FinderKind.Course, "freeCodeCamp", $"https://www.freecodecamp.org/news/search/?query={e}", "Free courses and full-length tutorials");
            Free(FinderKind.Course, "MIT OpenCourseWare", $"https://ocw.mit.edu/search/?q={e}", "Free MIT lectures and notes");
            Free(FinderKind.Course, "NPTEL", $"https://www.youtube.com/@nptelhrd/search?query={e}", "Free IIT/IISc lectures (NPTEL)");
        }
        if (any || q.Intent == FinderIntent.Book)
        {
            Buy(FinderKind.Book, "Amazon", $"https://www.{amazon}/s?k={e}&i=stripbooks", "Books and Kindle e-books");
            Buy(FinderKind.Book, "Google Play Books", $"https://play.google.com/store/search?q={e}&c=books", "E-books and audiobooks");
            Free(FinderKind.Book, "Open Library", $"https://openlibrary.org/search?q={e}", "Borrow e-books free (Internet Archive)");
            list.Add(new(FinderSection.FreeLegal, FinderKind.Book, "Browse free textbooks on OpenStax", "https://openstax.org/subjects", "OpenStax")
            { Detail = "Free, peer-reviewed college textbooks (PDF)", IsSearchLink = true, License = "CC BY" });
        }
        return Task.FromResult<IReadOnlyList<FinderResult>>(list);
    }
}

/// <summary>Steam store search: paid games with their price, plus free-to-play titles.</summary>
public sealed class SteamProvider(HttpClient http) : IFinderProvider
{
    public string Name => "Steam";

    public async Task<IReadOnlyList<FinderResult>> SearchAsync(FinderQuery q, CancellationToken ct)
    {
        if (q.Intent is FinderIntent.Course or FinderIntent.Book) return [];
        var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(q.Terms)}&l=english&cc={q.Region}";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct).ConfigureAwait(false));
        return Parse(doc.RootElement);
    }

    internal static IReadOnlyList<FinderResult> Parse(JsonElement root)
    {
        var list = new List<FinderResult>();
        if (!root.TryGetProperty("items", out var items)) return list;
        foreach (var it in items.EnumerateArray().Take(8))
        {
            if (it.TryGetProperty("type", out var type) && type.GetString() != "app") continue;
            var id = it.GetProperty("id").GetInt64();
            var name = it.GetProperty("name").GetString() ?? "";
            var page = $"https://store.steampowered.com/app/{id}/";
            var thumb = it.TryGetProperty("tiny_image", out var t) ? t.GetString() : null;
            if (it.TryGetProperty("price", out var price) && price.ValueKind == JsonValueKind.Object)
            {
                var cur = price.GetProperty("currency").GetString();
                var final = price.GetProperty("final").GetInt64() / 100m;
                list.Add(new(FinderSection.BuyOrRent, FinderKind.Game, name, page, "Steam")
                {
                    Price = $"{cur} {final.ToString("N2", CultureInfo.InvariantCulture)}", Detail = "PC game", ThumbnailUrl = thumb,
                });
            }
            else
            {
                list.Add(new(FinderSection.FreeLegal, FinderKind.Game, name, page, "Steam")
                {
                    Price = "Free", Detail = "Free to play or free demo, installs through Steam", License = "Free from the publisher", ThumbnailUrl = thumb,
                });
            }
        }
        return list;
    }
}

/// <summary>YouTube search: official free uploads, tutorials and full courses BDM can download.</summary>
public sealed class YouTubeFinderProvider(HttpClient http) : IFinderProvider
{
    public string Name => "YouTube";

    public async Task<IReadOnlyList<FinderResult>> SearchAsync(FinderQuery q, CancellationToken ct)
    {
        var yt = new YoutubeClient(http);
        var text = q.Intent == FinderIntent.Game ? q.Terms + " official trailer" : q.Text;
        var list = new List<FinderResult>();
        await foreach (var v in yt.Search.GetVideosAsync(text, ct).ConfigureAwait(false))
        {
            var kind = q.Intent == FinderIntent.Course || v.Duration is { TotalMinutes: > 40 } ? FinderKind.Course : FinderKind.Video;
            list.Add(new(FinderSection.FreeLegal, kind, v.Title, v.Url, "YouTube")
            {
                Detail = string.Join(" · ", new[] { v.Author.ChannelTitle, v.Duration is { } d ? Format.Duration(d) : null }.Where(s => !string.IsNullOrEmpty(s))),
                Price = "Free",
                License = "Uploaded to YouTube by the channel",
                ThumbnailUrl = v.Thumbnails.OrderBy(t => t.Resolution.Area).FirstOrDefault()?.Url,
                DownloadUrl = v.Url,
            });
            if (list.Count >= 8) break;
        }
        return list;
    }
}

/// <summary>Internet Archive items with an open licence or in the public domain; each also has an official torrent.</summary>
public sealed partial class InternetArchiveProvider(HttpClient http) : IFinderProvider
{
    public string Name => "Internet Archive";

    public async Task<IReadOnlyList<FinderResult>> SearchAsync(FinderQuery q, CancellationToken ct)
    {
        // Anyone can upload to the Internet Archive and pick a licence, so only curated public-domain and
        // openly licensed collections are searched, and never for games (no legal free copies of commercial games).
        if (q.Intent == FinderIntent.Game) return [];
        var words = Unsafe().Replace(q.Terms, " ").Trim();
        if (words.Length == 0) return [];
        var collections = q.Intent switch
        {
            FinderIntent.Book => BookCollections,
            FinderIntent.Course => CourseCollections,
            _ => [.. BookCollections, .. CourseCollections, .. MediaCollections],
        };
        var query = $"title:({words}) AND collection:({string.Join(" OR ", collections)})";
        var url = "https://archive.org/advancedsearch.php?q=" + Uri.EscapeDataString(query) +
                  "&fl[]=identifier&fl[]=title&fl[]=mediatype&fl[]=licenseurl&fl[]=creator&rows=8&output=json&sort[]=downloads+desc";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct).ConfigureAwait(false));
        return Parse(doc.RootElement);
    }

    internal static IReadOnlyList<FinderResult> Parse(JsonElement root)
    {
        var list = new List<FinderResult>();
        foreach (var d in root.GetProperty("response").GetProperty("docs").EnumerateArray())
        {
            var id = d.GetProperty("identifier").GetString();
            if (string.IsNullOrEmpty(id)) continue;
            var title = Str(d, "title") ?? id;
            var license = LicenseName(Str(d, "licenseurl")) ?? "Public domain";
            var kind = Str(d, "mediatype") switch
            {
                "texts" => FinderKind.Book,
                "movies" => FinderKind.Video,
                "audio" => FinderKind.Audio,
                "software" => FinderKind.Software,
                _ => FinderKind.Other,
            };
            var page = $"https://archive.org/details/{id}";
            var creator = Str(d, "creator");
            list.Add(new(FinderSection.FreeLegal, kind, title, page, "Internet Archive")
            {
                Detail = creator, Price = "Free", License = license, ThumbnailUrl = $"https://archive.org/services/img/{id}",
            });
            list.Add(new(FinderSection.LegalTorrent, kind, title, page, "Internet Archive")
            {
                Detail = "Official Internet Archive torrent", Price = "Free", License = license,
                DownloadUrl = $"https://archive.org/download/{id}/{id}_archive.torrent",
            });
        }
        return list;
    }

    // Public-domain books and audiobooks (Project Gutenberg, library scans of old books, LibriVox).
    private static readonly string[] BookCollections = ["gutenberg", "americana", "toronto", "librivoxaudio"];

    // Openly licensed lectures.
    private static readonly string[] CourseCollections = ["ucberkeley-webcast"];

    // Public-domain films, cartoons, radio, NASA media and live recordings shared with the artists' permission.
    private static readonly string[] MediaCollections = ["prelinger", "feature_films", "classic_cartoons", "oldtimeradio", "nasa", "etree"];

    private static string? Str(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? null
        : v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetString()).FirstOrDefault()
        : v.GetString();

    /// <summary>Readable licence name for public-domain and Creative Commons URLs; null for anything else.</summary>
    internal static string? LicenseName(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var u = url.ToLowerInvariant();
        if (u.Contains("publicdomain")) return "Public domain";
        var m = CcLicense().Match(u);
        return m.Success ? $"CC {m.Groups[1].Value.ToUpperInvariant()} {m.Groups[2].Value}" : null;
    }

    [GeneratedRegex(@"[^\p{L}\p{N} ]")]
    private static partial Regex Unsafe();

    [GeneratedRegex(@"creativecommons\.org/licenses/([a-z\-]+)/([\d.]+)")]
    private static partial Regex CcLicense();
}

/// <summary>Project Gutenberg (via Gutendex): free public-domain e-books with direct EPUB downloads.</summary>
public sealed class GutenbergProvider(HttpClient http) : IFinderProvider
{
    public string Name => "Project Gutenberg";

    public async Task<IReadOnlyList<FinderResult>> SearchAsync(FinderQuery q, CancellationToken ct)
    {
        if (q.Intent is FinderIntent.Game or FinderIntent.Course) return [];
        var text = q.Terms;
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://gutendex.com/books/?search={Uri.EscapeDataString(text)}", ct).ConfigureAwait(false));
        return Parse(doc.RootElement);
    }

    internal static IReadOnlyList<FinderResult> Parse(JsonElement root)
    {
        var list = new List<FinderResult>();
        foreach (var b in root.GetProperty("results").EnumerateArray().Take(6))
        {
            var id = b.GetProperty("id").GetInt64();
            var formats = b.GetProperty("formats");
            string? Fmt(string mime) => formats.TryGetProperty(mime, out var v) ? v.GetString() : null;
            var author = b.TryGetProperty("authors", out var a) ? a.EnumerateArray().Select(x => x.GetProperty("name").GetString()).FirstOrDefault() : null;
            list.Add(new(FinderSection.FreeLegal, FinderKind.Book, b.GetProperty("title").GetString() ?? $"Book {id}", $"https://www.gutenberg.org/ebooks/{id}", "Project Gutenberg")
            {
                Detail = author, Price = "Free", License = "Public domain",
                ThumbnailUrl = Fmt("image/jpeg"),
                DownloadUrl = Fmt("application/epub+zip") ?? Fmt("application/pdf"),
            });
        }
        return list;
    }
}
