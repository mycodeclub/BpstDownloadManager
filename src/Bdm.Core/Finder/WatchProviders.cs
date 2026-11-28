using System.Text.Json;

namespace Bdm.Core.Finder;

/// <summary>
/// Where a movie or series is legally available in the user's country: subscription streaming (Netflix, Prime Video,
/// JioHotstar, ZEE5, SonyLIV…), rent/buy and free-with-ads services. Data comes from TMDB, whose watch-provider lists
/// are supplied by JustWatch (both must be credited). Needs a free TMDB API key; without one this source is skipped
/// and <see cref="WatchLinksProvider"/> still offers direct links to the services.
/// </summary>
public sealed class TmdbWatchProvider(HttpClient http, Func<string?> apiKey) : IFinderProvider
{
    public const string CreditText = "Streaming availability data by JustWatch, via TMDB. This product uses the TMDB API but is not endorsed or certified by TMDB.";
    private const string Api = "https://api.themoviedb.org/3/";
    private const int MaxTitles = 3;

    public string Name => "Streaming services";

    public async Task<IReadOnlyList<FinderResult>> SearchAsync(FinderQuery q, CancellationToken ct)
    {
        if (q.Intent is not (FinderIntent.Movie or FinderIntent.Series or FinderIntent.Any)) return [];
        var key = apiKey()?.Trim();
        if (string.IsNullOrEmpty(key)) return [];

        var path = q.Intent switch { FinderIntent.Movie => "movie", FinderIntent.Series => "tv", _ => "multi" };
        using var search = JsonDocument.Parse(await GetAsync($"search/{path}?query={Uri.EscapeDataString(q.Terms)}&include_adult=false&language=en-US&page=1", key, ct).ConfigureAwait(false));
        var titles = ParseSearch(search.RootElement, q.Intent, q.Language);
        var lists = await Task.WhenAll(titles.Select(async t =>
        {
            using var doc = JsonDocument.Parse(await GetAsync($"{t.MediaType}/{t.Id}/watch/providers", key, ct).ConfigureAwait(false));
            return ParseProviders(doc.RootElement, q.Region, t);
        })).ConfigureAwait(false);
        return lists.SelectMany(l => l).ToList();
    }

    private async Task<string> GetAsync(string relative, string key, CancellationToken ct)
    {
        // v4 "API Read Access Token" (a JWT, starts with "eyJ") goes in the header; a v3 API key goes in the query.
        var bearer = key.StartsWith("eyJ", StringComparison.Ordinal);
        var url = Api + relative + (bearer ? "" : (relative.Contains('?') ? "&" : "?") + "api_key=" + Uri.EscapeDataString(key));
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (bearer) req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        req.Headers.Accept.ParseAdd("application/json");
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>A movie or series found on TMDB.</summary>
    internal sealed record Title(long Id, string MediaType, string Name, string? Year, string? OriginalLanguage, string? PosterPath);

    /// <summary>
    /// The best few matches, in TMDB's order, with titles in the wanted language first
    /// (so "Drishyam" in Hindi finds the Hindi film before the Malayalam original).
    /// </summary>
    internal static IReadOnlyList<Title> ParseSearch(JsonElement root, FinderIntent intent, string? language)
    {
        var list = new List<(Title Title, int Votes)>();
        if (!root.TryGetProperty("results", out var results)) return [];
        foreach (var r in results.EnumerateArray().Take(8))
        {
            var media = intent switch
            {
                FinderIntent.Movie => "movie",
                FinderIntent.Series => "tv",
                _ => Str(r, "media_type"),
            };
            if (media is not ("movie" or "tv")) continue; // people in multi-search
            var name = Str(r, media == "movie" ? "title" : "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var date = Str(r, media == "movie" ? "release_date" : "first_air_date");
            var votes = r.TryGetProperty("vote_count", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
            list.Add((new Title(r.GetProperty("id").GetInt64(), media, name, date is { Length: >= 4 } ? date[..4] : null,
                Str(r, "original_language"), Str(r, "poster_path")), votes));
        }
        // Without a category, only well-known titles: a search for "ubuntu" shouldn't list obscure films.
        if (intent == FinderIntent.Any) list = list.Where(x => x.Votes >= 50).ToList();
        else if (list.Count > 1) list = list.Where((x, i) => i == 0 || x.Votes > 0).ToList();
        return list.Take(5)
            .OrderBy(x => language is not null && x.Title.OriginalLanguage != language ? 1 : 0) // stable: keeps TMDB's order
            .Take(MaxTitles)
            .Select(x => x.Title)
            .ToList();
    }

    /// <summary>The services carrying <paramref name="t"/> in <paramref name="region"/>; one "not streaming" line if none.</summary>
    internal static IReadOnlyList<FinderResult> ParseProviders(JsonElement root, string region, Title t)
    {
        var kind = t.MediaType == "tv" ? FinderKind.Series : FinderKind.Movie;
        var label = (t.Year is null ? t.Name : $"{t.Name} ({t.Year})");
        var detail = string.Join(" · ", new[]
        {
            kind == FinderKind.Series ? "Series" : "Movie",
            t.OriginalLanguage is { } l ? "Original language: " + FinderFilters.LanguageName(l) : null,
        }.Where(s => s is not null));
        var thumb = t.PosterPath is null ? null : "https://image.tmdb.org/t/p/w154" + t.PosterPath;
        var tmdbPage = $"https://www.themoviedb.org/{t.MediaType}/{t.Id}/watch?locale={region}";

        var list = new List<FinderResult>();
        if (root.TryGetProperty("results", out var results) && results.TryGetProperty(region, out var country))
        {
            var link = Str(country, "link") ?? tmdbPage;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string category, FinderSection section, string price, string? license)
            {
                if (!country.TryGetProperty(category, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
                foreach (var p in arr.EnumerateArray())
                {
                    var service = Str(p, "provider_name");
                    if (string.IsNullOrEmpty(service) || !seen.Add(section + "|" + service)) continue;
                    var id = p.TryGetProperty("provider_id", out var pid) ? pid.GetRawText() : service;
                    list.Add(new(section, kind, label, ServiceUrl(service, t.Name) ?? $"{link}#provider-{id}", service)
                    {
                        Detail = detail, Price = price, License = license, ThumbnailUrl = thumb, Credit = CreditText,
                    });
                }
            }
            Add("flatrate", FinderSection.Watch, "Subscription", null);
            Add("free", FinderSection.FreeLegal, "Free", "Official free stream");
            Add("ads", FinderSection.FreeLegal, "Free with ads", "Official free stream");
            // Rent and buy on the same service become one "Rent or buy" result.
            var buy = Names(country, "buy");
            Add("rent", FinderSection.BuyOrRent, "Rent", null);
            foreach (var r in list.Where(x => x.Section == FinderSection.BuyOrRent && buy.Contains(x.Source)).ToList())
                list[list.IndexOf(r)] = r with { Price = "Rent or buy" };
            Add("buy", FinderSection.BuyOrRent, "Buy", null);
        }
        if (list.Count == 0)
            list.Add(new(FinderSection.Watch, kind, label, tmdbPage, "TMDB")
            {
                Detail = detail + " · Not on any streaming service in your country right now",
                ThumbnailUrl = thumb, Credit = CreditText,
            });
        return list;
    }

    private static HashSet<string> Names(JsonElement country, string category) =>
        country.TryGetProperty(category, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Select(p => Str(p, "provider_name")).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

    /// <summary>The service's own search page for a title, for services with a stable search address.</summary>
    internal static string? ServiceUrl(string service, string title)
    {
        var s = service.ToLowerInvariant();
        var e = Uri.EscapeDataString(title);
        if (s.Contains("netflix")) return $"https://www.netflix.com/search?q={e}";
        if (s.Contains("prime video") || s.Contains("amazon video")) return $"https://www.primevideo.com/search/ref=atv_nb_sr?phrase={e}";
        if (s.Contains("apple tv")) return $"https://tv.apple.com/search?term={e}";
        if (s.Contains("google play")) return $"https://play.google.com/store/search?q={e}&c=movies";
        if (s.Contains("youtube")) return $"https://www.youtube.com/results?search_query={e}";
        return null; // JustWatch's link (via TMDB) opens the title on the service
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// "Look it up on…" links for movies and series, needing no API key: JustWatch (every service in the user's
/// country at once) and the main streaming, rent and buy services.
/// </summary>
public sealed class WatchLinksProvider : IFinderProvider
{
    public string Name => "Streaming links";

    public Task<IReadOnlyList<FinderResult>> SearchAsync(FinderQuery q, CancellationToken ct)
    {
        if (!q.Intent.IsWatchIntent()) return Task.FromResult<IReadOnlyList<FinderResult>>([]);
        var kind = q.Intent == FinderIntent.Series ? FinderKind.Series : FinderKind.Movie;
        var e = Uri.EscapeDataString(q.Terms);
        var region = q.Region.ToLowerInvariant();
        var india = q.Region == "IN";
        var list = new List<FinderResult>();
        void Search(FinderSection section, string source, string url, string detail) =>
            list.Add(new(section, kind, $"Search \"{q.Terms}\" on {source}", url, source) { Detail = detail, IsSearchLink = true });
        void Open(FinderSection section, string source, string url, string detail) =>
            list.Add(new(section, kind, source, url, source) { Detail = detail, IsSearchLink = true });

        Search(FinderSection.Watch, "JustWatch", $"https://www.justwatch.com/{region}/search?q={e}", "Every streaming service in your country at once, with audio languages");
        Search(FinderSection.Watch, "Netflix", $"https://www.netflix.com/search?q={e}", "Subscription");
        Search(FinderSection.Watch, "Prime Video", $"https://www.primevideo.com/search/ref=atv_nb_sr?phrase={e}", "Subscription, rent or buy");
        if (india)
        {
            Open(FinderSection.Watch, "JioHotstar", "https://www.hotstar.com/in", $"Search there for \"{q.Terms}\"");
            Open(FinderSection.Watch, "ZEE5", "https://www.zee5.com", $"Search there for \"{q.Terms}\"");
            Open(FinderSection.Watch, "SonyLIV", "https://www.sonyliv.com", $"Search there for \"{q.Terms}\"");
        }
        Search(FinderSection.Watch, "Apple TV", $"https://tv.apple.com/search?term={e}", "Subscription, rent or buy");
        Search(FinderSection.BuyOrRent, "Google TV", $"https://play.google.com/store/search?q={e}&c=movies", "Rent or buy movies and TV");
        return Task.FromResult<IReadOnlyList<FinderResult>>(list);
    }
}
