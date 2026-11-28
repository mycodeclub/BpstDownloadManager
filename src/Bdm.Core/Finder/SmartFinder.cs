using System.Globalization;

namespace Bdm.Core.Finder;

/// <summary>
/// Smart Finder: one search for download links of games, software, e-books, documents and other files:
/// official stores (buy) first, then free legal downloads (open-source releases, free games, public-domain and
/// openly licensed files) and official torrents. For movies and series it shows where they stream legally
/// (OTT services, rent/buy, free with ads), with a language filter. Piracy sites are filtered out.
/// </summary>
public sealed class SmartFinder
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(20);

    // Checked in this order: the first group with a match wins ("short film" before "film", "pc game" before "pc").
    private static readonly (FinderIntent Intent, string[] Words)[] IntentWords =
    [
        (FinderIntent.Document, ["research paper", "paper", "papers", "thesis", "journal", "report", "whitepaper", "manual", "document", "documents"]),
        (FinderIntent.Book, ["book", "books", "ebook", "e-book", "pdf", "epub", "novel", "textbook", "mobi", "kindle"]),
        (FinderIntent.Course, ["course", "courses", "tutorial", "tutorials", "lecture", "lectures", "learn", "learning", "class", "classes",
                               "training", "bootcamp", "masterclass", "udemy", "coursera", "nptel", "lesson", "lessons", "crash course"]),
        (FinderIntent.Game, ["game", "games", "pc game", "gta", "ps5", "ps4", "xbox", "nintendo", "fifa", "minecraft", "call of duty",
                             "valorant", "fortnite", "elden ring", "cyberpunk", "red dead", "battlefield", "gaming"]),
        (FinderIntent.Software, ["software", "app", "apps", "apk", "windows", "program", "tool", "tools", "editor", "antivirus", "vpn",
                                 "exe", "installer", "for pc", "for android", "for mac", "linux", "open source", "iso", "distro", "ubuntu", "driver"]),
        (FinderIntent.Audio, ["song", "songs", "music", "mp3", "album", "audiobook", "audiobooks", "podcast", "soundtrack", "audio"]),
        (FinderIntent.Series, ["web series", "webseries", "tv series", "tv show", "tv shows", "series", "season", "episode", "episodes",
                               "anime", "k-drama", "kdrama", "sitcom"]),
        (FinderIntent.Movie, ["movie", "movies", "film", "films", "full movie", "bollywood", "hollywood", "tollywood", "kollywood",
                              "dubbed", "ott", "netflix", "prime video", "hotstar", "jiohotstar", "zee5", "sonyliv"]),
    ];

    // Language names (and common spellings) that pick the language filter when typed ("die hard 4 in hindi").
    private static readonly (string Code, string[] Names)[] LanguageWords =
    [
        ("hi", ["hindi"]), ("en", ["english"]), ("ta", ["tamil"]), ("te", ["telugu"]), ("ml", ["malayalam"]),
        ("kn", ["kannada"]), ("bn", ["bengali", "bangla"]), ("mr", ["marathi"]), ("pa", ["punjabi"]), ("gu", ["gujarati"]),
        ("ko", ["korean"]), ("ja", ["japanese"]), ("es", ["spanish"]), ("fr", ["french"]),
    ];

    // Removed from movie/series searches: TMDB and the OTT apps search by title only. Kept small on purpose,
    // so titles like "The Truman Show" or "Lost in Translation" stay intact.
    private static readonly string[] WatchNoisePhrases =
        ["web series", "tv series", "tv shows", "tv show", "full movies", "full movie", "watch online", "prime video", "all seasons", "all episodes", "free download"];

    private static readonly HashSet<string> WatchNoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "dubbed", "dub", "ott", "webseries", "streaming", "movie", "movies", "film", "films", "full", "hd", "1080p", "720p", "480p", "4k",
        "download", "free", "torrent", "torrents", "bollywood", "hollywood", "tollywood", "kollywood",
        "netflix", "hotstar", "jiohotstar", "zee5", "sonyliv", "season", "seasons", "episode", "episodes",
    };

    private static readonly string[] NoisePhrases =
        ["web series", "short films", "short film", "full movie", "research paper", "pc game", "for pc", "free download", "tv series", "tv show"];

    private static readonly HashSet<string> NoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "games", "pc", "pdf", "epub", "mobi", "ebook", "e-book", "ebooks", "book", "books", "free", "download",
        "downloads", "torrent", "torrents", "full", "course", "courses", "tutorial", "tutorials", "lecture", "lectures",
        "movie", "movies", "film", "films", "webseries", "software", "apk", "mp3", "song", "songs",
        "audiobook", "paper", "papers", "document", "documents", "crack", "cracked", "hd", "1080p", "720p", "4k",
        "latest", "last", "new", "on", "by", "of", "the", "for",
    };

    private readonly IReadOnlyList<IFinderProvider> _providers;

    public SmartFinder(IEnumerable<IFinderProvider> providers) => _providers = providers.ToList();

    /// <param name="tmdbApiKey">TMDB API key (v3 key or v4 read token) for "where to watch" data; without one, movie
    /// and series searches still get direct links to the streaming services.</param>
    public static SmartFinder CreateDefault(HttpClient http, Func<string?>? tmdbApiKey = null) => new(
    [
        new TmdbWatchProvider(http, tmdbApiKey ?? (() => Environment.GetEnvironmentVariable("BDM_TMDB_KEY"))),
        new WatchLinksProvider(),
        new StoreLinksProvider(),
        new SteamProvider(http),
        new GogProvider(http),
        new InternetArchiveProvider(http),
        new GutenbergProvider(http),
        new GitHubProvider(http),
        new ArxivProvider(http),
        new OfficialTorrentsProvider(),
    ]);

    public IReadOnlyList<string> SourceNames => _providers.Select(p => p.Name).ToList();

    public static FinderIntent DetectIntent(string text)
    {
        var t = " " + string.Join(' ', text.ToLowerInvariant().Split((char[])[' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries)) + " ";
        foreach (var (intent, words) in IntentWords)
            if (words.Any(w => t.Contains(" " + w + " ", StringComparison.Ordinal)))
                return intent;
        return FinderIntent.Any;
    }

    /// <summary>The query without noise words ("GTA 6 game pdf free" → "GTA 6"); the original if nothing is left.</summary>
    public static string CoreTerms(string text)
    {
        foreach (var phrase in NoisePhrases)
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\b" + phrase + @"\b", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var words = text.Split((char[])[' ', ',', '	'], StringSplitOptions.RemoveEmptyEntries).Where(w => !NoiseWords.Contains(w)).ToList();
        return words.Count == 0 ? text.Trim() : string.Join(' ', words);
    }

    /// <summary>Language picked by a word in the query ("in hindi", "tamil dubbed" → "hi", "ta"), or null.</summary>
    public static string? DetectLanguage(string text)
    {
        var words = text.ToLowerInvariant().Split((char[])[' ', ',', '\t', '-', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var (code, names) in LanguageWords)
            if (names.Any(n => words.Contains(n)))
                return code;
        return null;
    }

    /// <summary>
    /// The title to search streaming services for: "die hard 4 in hindi" → "die hard 4",
    /// "Mirzapur season 2 web series" → "Mirzapur". The original if nothing is left.
    /// </summary>
    public static string WatchTerms(string text)
    {
        const System.Text.RegularExpressions.RegexOptions I = System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        var languageNames = LanguageWords.SelectMany(l => l.Names).ToList();
        var t = System.Text.RegularExpressions.Regex.Replace(text, @"\b(season|episode|ep)\s*\d+\b", " ", I);
        // A language is a filter only next to "in"/"dubbed" or at the end ("in hindi", "tamil dubbed", "… hindi"),
        // so it stays in titles like "Hindi Medium" or "English Vinglish".
        var lang = "(" + string.Join('|', languageNames) + ")";
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\b(dubbed\s+)?in\s+" + lang + @"\b", " ", I);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\b" + lang + @"\s+(dubbed|dub|audio|version)\b", " ", I);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"(?<=\S\s+)" + lang + @"\s*$", " ", I);
        foreach (var phrase in WatchNoisePhrases)
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\b" + phrase + @"\b", " ", I);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\bseries\s*$", " ", I);
        var words = t.Split((char[])[' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !WatchNoiseWords.Contains(w)).ToList();
        return words.Count == 0 ? text.Trim() : string.Join(' ', words);
    }

    public static string CurrentRegion()
    {
        try { return RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant(); }
        catch (ArgumentException) { return "US"; }
    }

    /// <summary>
    /// Searches every source at once. <paramref name="onUpdate"/> (optional) gets the combined results each time
    /// a source answers, so a slow source doesn't hold up the others. <paramref name="category"/> is the category the
    /// user picked; null means "guess it from the words".
    /// </summary>
    /// <param name="language">Wanted language for movies and series (ISO 639-1); null = taken from the words, if any.</param>
    public async Task<FinderResults> SearchAsync(string text, CancellationToken ct = default, string? region = null,
        Action<FinderResults>? onUpdate = null, FinderIntent? category = null, string? language = null)
    {
        text = text.Trim();
        language ??= DetectLanguage(text);
        var intent = category ?? DetectIntent(text);
        if (category is null && intent == FinderIntent.Any && language is not null) intent = FinderIntent.Movie; // "die hard 4 in hindi"
        var query = new FinderQuery(text, intent, region ?? CurrentRegion())
        {
            Terms = intent.IsWatchIntent() ? WatchTerms(text) : CoreTerms(text),
            Language = intent.IsWatchIntent() || intent == FinderIntent.Any ? language : null,
        };
        if (text.Length == 0) return new FinderResults { Query = text, Intent = query.Intent };

        var answers = new (IReadOnlyList<FinderResult>? Items, bool Failed)[_providers.Count];
        var gate = new object();
        var tasks = _providers.Select(async (p, index) =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProviderTimeout);
            (IReadOnlyList<FinderResult>, bool) answer;
            try { answer = (await p.SearchAsync(query, cts.Token).ConfigureAwait(false), false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { answer = ([], true); }
            FinderResults? snapshot = null;
            lock (gate)
            {
                answers[index] = answer;
                if (onUpdate is not null) snapshot = Combine(query, answers, final: false);
            }
            if (snapshot is not null) onUpdate!(snapshot);
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return Combine(query, answers, final: true);
    }

    private FinderResults Combine(FinderQuery query, (IReadOnlyList<FinderResult>? Items, bool Failed)[] answers, bool final)
    {
        var results = new FinderResults { Query = query.Text, Intent = query.Intent, IsComplete = final || answers.All(a => a.Items is not null) };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = answers
            .SelectMany((a, providerIndex) => (a.Items ?? []).Select((item, i) => (item, providerIndex, i)))
            .Where(x => !SourceGuard.IsBlocked(x.item.Url) && !SourceGuard.IsBlocked(x.item.DownloadUrl) && (x.item.IsSearchLink || !SourceGuard.LooksPirated(x.item.Title)))
            .OrderBy(x => x.item.Section)
            .ThenBy(x => Matches(x.item.Kind, query.Intent) ? 0 : 1)
            .ThenBy(x => x.item.IsSearchLink ? 1 : 0)
            .ThenBy(x => x.providerIndex)
            .ThenBy(x => x.i)
            .Select(x => x.item);
        foreach (var item in ordered)
            if (seen.Add(item.Section + "|" + (item.DownloadUrl ?? item.Url)))
                results.Items.Add(item);

        results.Credits.AddRange(results.Items.Select(i => i.Credit).OfType<string>().Distinct());
        results.FailedSources.AddRange(answers.Select((a, i) => (a, i)).Where(x => x.a.Failed).Select(x => _providers[x.i].Name));
        results.PendingSources.AddRange(answers.Select((a, i) => (a, i)).Where(x => x.a.Items is null).Select(x => _providers[x.i].Name));

        if (query.Intent.IsWatchIntent())
        {
            if (results.IsComplete)
            {
                if (query.Language is { } lang)
                    results.Notices.Add($"{FinderFilters.LanguageName(lang)} audio: streaming services don't publish which dubs they carry. Open the title and check its Audio & Subtitles (or Languages) menu.");
                if (!results.Items.Any(i => !i.IsSearchLink && i.Section is FinderSection.Watch or FinderSection.BuyOrRent))
                    results.Notices.Add("Use the service links below to look it up. Films on streaming services are copy-protected: watch them in the service's app, and use the app's own Download button to watch offline.");
            }
            return results;
        }

        var freeCopies = results.In(FinderSection.FreeLegal).Where(i => !i.IsSearchLink && (query.Intent != FinderIntent.Game || i.Kind == FinderKind.Game));
        if (results.IsComplete && !freeCopies.Any())
            results.Notices.Add(query.Intent == FinderIntent.Game
                ? "No legal free download of this game was found. Paid games are only legally available from official stores; free-to-play and open-source games are listed under Free when there are any."
                : query.Intent == FinderIntent.Software
                    ? "No free download was found. Paid software is only legally available from its maker or the official stores; open-source alternatives show up under Free when there are any."
                    : "No legal free copy was found. Buy it from the official sources, or try the free platforms listed.");
        return results;
    }

    internal static bool Matches(FinderKind kind, FinderIntent intent) => intent switch
    {
        FinderIntent.Game => kind is FinderKind.Game,
        FinderIntent.Software => kind is FinderKind.Software,
        FinderIntent.Course => kind is FinderKind.Course,
        FinderIntent.Book => kind is FinderKind.Book,
        FinderIntent.Document => kind is FinderKind.Document,
        FinderIntent.Audio => kind is FinderKind.Audio,
        FinderIntent.Movie => kind is FinderKind.Movie,
        FinderIntent.Series => kind is FinderKind.Series,
        _ => true,
    };
}
