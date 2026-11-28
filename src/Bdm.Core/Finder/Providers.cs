using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

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

        var region = q.Region.ToLowerInvariant();
        bool any = q.Intent == FinderIntent.Any;
        bool Is(params FinderIntent[] intents) => any || intents.Contains(q.Intent);

        if (Is(FinderIntent.Game))
        {
            Buy(FinderKind.Game, "Epic Games Store", $"https://store.epicgames.com/en-US/browse?q={e}", "PC games");
            Buy(FinderKind.Game, "PlayStation Store", $"https://store.playstation.com/en-{(q.Region == "IN" ? "in" : "us")}/search/{e}", "PS5 / PS4");
            Buy(FinderKind.Game, "Xbox Store", $"https://www.xbox.com/en-US/Search/Results?q={e}", "Xbox and PC Game Pass");
            Free(FinderKind.Game, "Epic free games", "https://store.epicgames.com/en-US/free-games", "Free games every week, yours to keep");
            Free(FinderKind.Game, "itch.io", $"https://itch.io/search?q={e}", "Indie games; many are free downloads from their developers");
            Free(FinderKind.Game, "GOG free games", $"https://www.gog.com/en/games?priceRange=0,0&query={e}", "Free DRM-free game installers");
            Buy(FinderKind.Game, "GOG", $"https://www.gog.com/en/games?query={e}", "DRM-free PC games: the installer is yours to keep");
            Buy(FinderKind.Game, "Humble Store", $"https://www.humblebundle.com/store/search?search={e}", "PC games and bundles, part of the money goes to charity");
            Free(FinderKind.Game, "Game Jolt", $"https://gamejolt.com/search?q={e}", "Free indie games, downloaded from their developers");
            list.Add(new(FinderSection.FreeLegal, FinderKind.Game, "Browse IndieGala free games", "https://freebies.indiegala.com/", "IndieGala")
            { Detail = "Free games given away by their developers", IsSearchLink = true, License = "Free from the developer" });
        }
        if (Is(FinderIntent.Software))
        {
            Buy(FinderKind.Software, "Microsoft Store", $"https://apps.microsoft.com/search?query={e}", "Windows apps, free and paid");
            Buy(FinderKind.Software, "Google Play", $"https://play.google.com/store/search?q={e}&c=apps", "Android apps, free and paid");
            Free(FinderKind.Software, "F-Droid", $"https://search.f-droid.org/?q={e}", "Free and open-source Android apps");
            Free(FinderKind.Software, "Flathub", $"https://flathub.org/apps/search?q={e}", "Free Linux apps");
        }
        if (Is(FinderIntent.Course))
        {
            Buy(FinderKind.Course, "Udemy", $"https://www.udemy.com/courses/search/?q={e}", "Paid courses, frequent sales");
            Buy(FinderKind.Course, "Coursera", $"https://www.coursera.org/search?query={e}", "Many courses can be audited for free");
            Free(FinderKind.Course, "freeCodeCamp", $"https://www.freecodecamp.org/news/search/?query={e}", "Free courses and full-length tutorials");
            Free(FinderKind.Course, "MIT OpenCourseWare", $"https://ocw.mit.edu/search/?q={e}", "Free MIT lectures and notes");
            Free(FinderKind.Course, "NPTEL", $"https://www.youtube.com/@nptelhrd/search?query={e}", "Free IIT/IISc lectures (NPTEL)");
        }
        if (Is(FinderIntent.Book))
        {
            Buy(FinderKind.Book, "Amazon", $"https://www.{amazon}/s?k={e}&i=stripbooks", "Books and Kindle e-books");
            Buy(FinderKind.Book, "Google Play Books", $"https://play.google.com/store/search?q={e}&c=books", "E-books and audiobooks");
            Free(FinderKind.Book, "Open Library", $"https://openlibrary.org/search?q={e}", "Borrow e-books free (Internet Archive)");
            list.Add(new(FinderSection.FreeLegal, FinderKind.Book, "Browse free textbooks on OpenStax", "https://openstax.org/subjects", "OpenStax")
            { Detail = "Free, peer-reviewed college textbooks (PDF)", IsSearchLink = true, License = "CC BY" });
        }
        if (q.Intent == FinderIntent.Document)
        {
            Free(FinderKind.Document, "Google Scholar", $"https://scholar.google.com/scholar?q={e}", "Research papers; many have a free PDF");
            Free(FinderKind.Document, "CORE", $"https://core.ac.uk/search?q={e}", "Millions of open-access research papers");
            Free(FinderKind.Document, "Semantic Scholar", $"https://www.semanticscholar.org/search?q={e}", "Papers with free PDFs where available");
        }
        if (q.Intent == FinderIntent.Audio)
        {
            Buy(FinderKind.Audio, "Bandcamp", $"https://bandcamp.com/search?q={e}", "Buy music straight from artists (some free)");
            Buy(FinderKind.Audio, "Amazon Music", $"https://music.{amazon}/search/{e}", "Buy or stream");
            Free(FinderKind.Audio, "Free Music Archive", $"https://freemusicarchive.org/search?quicksearch={e}", "Free, legal music downloads");
            Free(FinderKind.Audio, "Jamendo", $"https://www.jamendo.com/search?q={e}", "Free Creative Commons music");
            Free(FinderKind.Audio, "LibriVox", $"https://librivox.org/search?q={e}&search_form=advanced", "Free public-domain audiobooks");
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
        if (q.Intent is not (FinderIntent.Any or FinderIntent.Game or FinderIntent.Software)) return [];
        var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(q.Terms)}&l=english&cc={q.Region}";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct).ConfigureAwait(false));
        return Parse(doc.RootElement, q.Intent == FinderIntent.Software ? FinderKind.Software : FinderKind.Game);
    }

    internal static IReadOnlyList<FinderResult> Parse(JsonElement root, FinderKind kind = FinderKind.Game)
    {
        var what = kind == FinderKind.Software ? "PC software" : "PC game";
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
                list.Add(new(FinderSection.BuyOrRent, kind, name, page, "Steam")
                {
                    Price = $"{cur} {final.ToString("N2", CultureInfo.InvariantCulture)}", Detail = what, ThumbnailUrl = thumb,
                });
            }
            else
            {
                list.Add(new(FinderSection.FreeLegal, kind, name, page, "Steam")
                {
                    Price = "Free", Detail = "Free to play or free demo, installs through Steam", License = "Free from the publisher", ThumbnailUrl = thumb,
                });
            }
        }
        return list;
    }
}

/// <summary>GOG store search: DRM-free PC games with their price; free games come with their own installers.</summary>
public sealed class GogProvider(HttpClient http) : IFinderProvider
{
    public string Name => "GOG";

    public async Task<IReadOnlyList<FinderResult>> SearchAsync(FinderQuery q, CancellationToken ct)
    {
        if (q.Intent is not (FinderIntent.Any or FinderIntent.Game)) return [];
        var url = $"https://catalog.gog.com/v1/catalog?query=like:{Uri.EscapeDataString(q.Terms)}&limit=6&order=desc:score&countryCode={q.Region}&locale=en-US";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct).ConfigureAwait(false));
        return Parse(doc.RootElement);
    }

    internal static IReadOnlyList<FinderResult> Parse(JsonElement root)
    {
        var list = new List<FinderResult>();
        if (!root.TryGetProperty("products", out var products)) return list;
        foreach (var p in products.EnumerateArray().Take(6))
        {
            var title = p.TryGetProperty("title", out var t) ? t.GetString() : null;
            var page = p.TryGetProperty("storeLink", out var l) ? l.GetString() : null;
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(page)) continue;
            var thumb = p.TryGetProperty("coverHorizontal", out var c) ? c.GetString() : null;
            var dev = p.TryGetProperty("developers", out var d) && d.ValueKind == JsonValueKind.Array ? d.EnumerateArray().Select(x => x.GetString()).FirstOrDefault() : null;
            decimal? amount = null;
            string? currency = null;
            if (p.TryGetProperty("price", out var price) && price.ValueKind == JsonValueKind.Object &&
                price.TryGetProperty("finalMoney", out var money) && money.ValueKind == JsonValueKind.Object)
            {
                if (money.TryGetProperty("amount", out var am) &&
                    decimal.TryParse(am.ValueKind == JsonValueKind.Number ? am.GetRawText() : am.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var a))
                    amount = a;
                currency = money.TryGetProperty("currency", out var cur) ? cur.GetString() : null;
            }
            if (amount is null) continue; // not for sale (coming soon, delisted)
            var detail = string.Join(" · ", new[] { dev, "DRM-free PC game" }.Where(s => !string.IsNullOrEmpty(s)));
            list.Add(amount == 0
                ? new(FinderSection.FreeLegal, FinderKind.Game, title, page, "GOG")
                {
                    Detail = detail + ", free installer after signing in", Price = "Free", License = "Free from the publisher", ThumbnailUrl = thumb,
                }
                : new(FinderSection.BuyOrRent, FinderKind.Game, title, page, "GOG")
                {
                    Detail = detail, Price = $"{currency} {amount.Value.ToString("N2", CultureInfo.InvariantCulture)}", ThumbnailUrl = thumb,
                });
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
        string[] collections = q.Intent switch
        {
            FinderIntent.Book => BookCollections,
            FinderIntent.Course => CourseCollections,
            FinderIntent.Document => ["governmentpublications", "fedlink"],
            FinderIntent.Audio => AudioCollections,
            FinderIntent.Movie => FilmCollections,
            FinderIntent.Series => TvCollections,
            FinderIntent.Any => [.. BookCollections, .. CourseCollections, .. AudioCollections],
            _ => [], // games and software: no trustworthy free copies there
        };
        var words = Unsafe().Replace(q.Terms, " ").Trim();
        if (collections.Length == 0 || words.Length == 0) return [];
        var query = $"title:({words}) AND collection:({string.Join(" OR ", collections)})";
        var url = "https://archive.org/advancedsearch.php?q=" + Uri.EscapeDataString(query) +
                  "&fl[]=identifier&fl[]=title&fl[]=mediatype&fl[]=licenseurl&fl[]=creator&fl[]=year&rows=8&output=json&sort[]=downloads+desc";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct).ConfigureAwait(false));
        return Parse(doc.RootElement, q.Intent);
    }

    internal static IReadOnlyList<FinderResult> Parse(JsonElement root, FinderIntent intent = FinderIntent.Any)
    {
        var list = new List<FinderResult>();
        foreach (var d in root.GetProperty("response").GetProperty("docs").EnumerateArray())
        {
            var id = d.GetProperty("identifier").GetString();
            if (string.IsNullOrEmpty(id)) continue;
            var title = Str(d, "title") ?? id;
            var openLicense = LicenseName(Str(d, "licenseurl"));
            if (intent.IsWatchIntent() && openLicense is null && !IsPublicDomainByAge(Str(d, "year"))) continue;
            var license = openLicense ?? "Public domain";
            var kind = Str(d, "mediatype") switch
            {
                "texts" => intent == FinderIntent.Document ? FinderKind.Document : FinderKind.Book,
                "movies" => intent switch
                {
                    FinderIntent.Movie => FinderKind.Movie,
                    FinderIntent.Series => FinderKind.Series,
                    _ => FinderKind.Course, // otherwise only lecture collections are searched
                },
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

    // Films and TV, kept only when openly licensed or old enough to be in the public domain (see IsPublicDomainByAge):
    // anyone can upload to these collections, so the collection alone isn't proof.
    private static readonly string[] FilmCollections = ["feature_films", "silent_films", "film_noir"];
    private static readonly string[] TvCollections = ["classic_tv"];

    /// <summary>
    /// Published more than 95 years ago, so out of copyright in the US and most of the world
    /// (in 2026: 1930 and earlier). Conservative on purpose: newer public-domain films need an explicit licence.
    /// </summary>
    internal static bool IsPublicDomainByAge(string? year, int? currentYear = null)
    {
        var digits = (year ?? "").Trim();
        if (digits.Length > 4) digits = digits[..4];
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y > 1800 && y <= (currentYear ?? DateTime.UtcNow.Year) - 96;
    }

    // Openly licensed lectures.
    private static readonly string[] CourseCollections = ["ucberkeley-webcast"];

    // Public-domain audiobooks and radio, and live recordings shared with the artists' permission.
    private static readonly string[] AudioCollections = ["librivoxaudio", "oldtimeradio", "etree"];

    private static string? Str(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? null
        : v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(Text).FirstOrDefault()
        : Text(v);

    // Archive metadata mixes strings and numbers ("year": 1922 or "1922").
    private static string? Text(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        _ => null,
    };

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
        if (q.Intent is not (FinderIntent.Any or FinderIntent.Book)) return [];
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

/// <summary>Open-source software and games on GitHub (licensed for free use), with their release files.</summary>
public sealed class GitHubProvider(HttpClient http) : IFinderProvider
{
    public string Name => "GitHub";

    public async Task<IReadOnlyList<FinderResult>> SearchAsync(FinderQuery q, CancellationToken ct)
    {
        if (q.Intent is not (FinderIntent.Software or FinderIntent.Game)) return [];
        var search = q.Terms + " in:name,description" + (q.Intent == FinderIntent.Game ? " topic:game" : "") + $" stars:>={MinStars}";
        using var doc = JsonDocument.Parse(await GetAsync(http, $"https://api.github.com/search/repositories?q={Uri.EscapeDataString(search)}&sort=stars&per_page=10", ct).ConfigureAwait(false));
        return Parse(doc.RootElement, q.Intent == FinderIntent.Game ? FinderKind.Game : FinderKind.Software, q.Terms);
    }

    // Skips throwaway and scam repositories ("GTA 6 free download" uploads have few or no stars).
    internal const int MinStars = 20;

    private static readonly string[] NonDownloadExtensions = [".yml", ".yaml", ".txt", ".json", ".sha256", ".sha512", ".sig", ".asc", ".blockmap", ".md5", ".pem"];

    /// <summary>The files of a project's latest release (installers, archives, packages), largest first.</summary>
    public static async Task<IReadOnlyList<FinderFile>> GetReleaseFilesAsync(HttpClient http, string releaseApiUrl, CancellationToken ct = default)
    {
        string json;
        try { json = await GetAsync(http, releaseApiUrl, ct).ConfigureAwait(false); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return []; } // no releases
        using var doc = JsonDocument.Parse(json);
        return ParseRelease(doc.RootElement);
    }

    internal static IReadOnlyList<FinderFile> ParseRelease(JsonElement release) =>
        release.TryGetProperty("assets", out var assets)
            ? assets.EnumerateArray()
                .Select(a => new FinderFile(a.GetProperty("name").GetString() ?? "", a.GetProperty("size").GetInt64(), a.GetProperty("browser_download_url").GetString() ?? ""))
                .Where(f => f.Url.Length > 0 && !NonDownloadExtensions.Any(x => f.Name.EndsWith(x, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(f => f.Size)
                .ToList()
            : [];

    private static async Task<string> GetAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("BDM-SmartFinder");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    internal static IReadOnlyList<FinderResult> Parse(JsonElement root, FinderKind kind = FinderKind.Software, string? terms = null)
    {
        // GitHub also matches topics and READMEs; keep only projects whose name or description has every word.
        var words = (terms ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1).ToList();
        var list = new List<FinderResult>();
        foreach (var r in root.GetProperty("items").EnumerateArray())
        {
            var about = ((r.GetProperty("full_name").GetString() ?? "") + " " + (r.TryGetProperty("description", out var dd) ? dd.GetString() : "")).ToLowerInvariant();
            if (!words.All(w => about.Contains(w, StringComparison.Ordinal))) continue;
            var spdx = r.TryGetProperty("license", out var lic) && lic.ValueKind == JsonValueKind.Object && lic.TryGetProperty("spdx_id", out var id) ? id.GetString() : null;
            if (string.IsNullOrEmpty(spdx) || spdx == "NOASSERTION") continue; // no licence = not free to use
            var url = r.GetProperty("html_url").GetString()!;
            var stars = r.GetProperty("stargazers_count").GetInt32();
            var desc = r.TryGetProperty("description", out var d) ? d.GetString() : null;
            var fullName = r.GetProperty("full_name").GetString();
            list.Add(new(FinderSection.FreeLegal, kind, r.GetProperty("name").GetString() ?? url, url + "/releases/latest", "GitHub")
            {
                ReleaseFilesUrl = $"https://api.github.com/repos/{fullName}/releases/latest",
                Detail = string.Join(" · ", new[] { r.GetProperty("owner").GetProperty("login").GetString(), $"★ {stars:N0}", desc }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Price = "Free", License = $"Open source ({spdx})",
                ThumbnailUrl = r.GetProperty("owner").GetProperty("avatar_url").GetString(),
            });
            if (list.Count >= 6) break;
        }
        return list;
    }
}

/// <summary>arXiv: open-access research papers with direct PDF downloads.</summary>
public sealed class ArxivProvider(HttpClient http) : IFinderProvider
{
    private static readonly System.Xml.Linq.XNamespace Atom = "http://www.w3.org/2005/Atom";

    public string Name => "arXiv";

    public async Task<IReadOnlyList<FinderResult>> SearchAsync(FinderQuery q, CancellationToken ct)
    {
        if (q.Intent != FinderIntent.Document) return [];
        var url = "https://export.arxiv.org/api/query?search_query=" + Uri.EscapeDataString($"all:\"{q.Terms}\"") + "&max_results=6&sortBy=relevance";
        return Parse(await http.GetStringAsync(url, ct).ConfigureAwait(false));
    }

    internal static IReadOnlyList<FinderResult> Parse(string xml)
    {
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        var list = new List<FinderResult>();
        foreach (var e in doc.Root!.Elements(Atom + "entry"))
        {
            var title = string.Join(' ', ((string?)e.Element(Atom + "title") ?? "").Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));
            var page = e.Elements(Atom + "link").FirstOrDefault(l => (string?)l.Attribute("rel") == "alternate")?.Attribute("href")?.Value ?? (string?)e.Element(Atom + "id");
            if (string.IsNullOrEmpty(page) || title.Length == 0) continue;
            var pdf = e.Elements(Atom + "link").FirstOrDefault(l => (string?)l.Attribute("title") == "pdf")?.Attribute("href")?.Value;
            var authors = e.Elements(Atom + "author").Select(a => (string?)a.Element(Atom + "name")).Where(n => n is not null).ToList();
            var year = ((string?)e.Element(Atom + "published"))?[..4];
            var who = authors.Count switch { 0 => null, 1 => authors[0], _ => authors[0] + " et al." };
            list.Add(new(FinderSection.FreeLegal, FinderKind.Document, title, page, "arXiv")
            {
                Detail = string.Join(" · ", new[] { who, year }.Where(s => !string.IsNullOrEmpty(s))),
                Price = "Free", License = "Open access",
                DownloadUrl = pdf is null ? null : pdf + ".pdf",
            });
        }
        return list;
    }
}

/// <summary>Official torrents published by Linux distributions (free to share by design).</summary>
public sealed class OfficialTorrentsProvider : IFinderProvider
{
    private static readonly (string[] Keys, string Name, string Url)[] Distros =
    [
        (["ubuntu", "kubuntu", "xubuntu", "lubuntu"], "Ubuntu", "https://ubuntu.com/download/alternative-downloads"),
        (["debian"], "Debian", "https://www.debian.org/CD/torrent-cd/"),
        (["fedora"], "Fedora", "https://torrent.fedoraproject.org/"),
        (["arch", "archlinux"], "Arch Linux", "https://archlinux.org/download/"),
        (["mint"], "Linux Mint", "https://linuxmint.com/download.php"),
        (["kali"], "Kali Linux", "https://www.kali.org/get-kali/"),
    ];

    private static readonly string[] AnyDistro = ["linux", "distro", "iso", "os"];

    public string Name => "Official torrents";

    public Task<IReadOnlyList<FinderResult>> SearchAsync(FinderQuery q, CancellationToken ct)
    {
        if (q.Intent is not (FinderIntent.Any or FinderIntent.Software)) return Task.FromResult<IReadOnlyList<FinderResult>>([]);
        var words = q.Text.ToLowerInvariant().Split((char[])[' ', ',', '-'], StringSplitOptions.RemoveEmptyEntries);
        var all = words.Any(w => AnyDistro.Contains(w));
        var list = Distros
            .Where(d => all || d.Keys.Any(k => words.Contains(k)))
            .Select(d => new FinderResult(FinderSection.LegalTorrent, FinderKind.Software, $"{d.Name}: official torrents", d.Url, d.Name)
            {
                Detail = "Torrent and magnet links on the official download page", Price = "Free", License = "Free and open source",
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<FinderResult>>(list);
    }
}
