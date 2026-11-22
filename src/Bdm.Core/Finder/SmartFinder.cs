using System.Globalization;

namespace Bdm.Core.Finder;

/// <summary>
/// Smart Finder: one search for download links of games, software, e-books, documents and other files:
/// official stores (buy) first, then free legal downloads (open-source releases, free games, public-domain and
/// openly licensed files) and official torrents. Piracy sites are filtered out.
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
    ];

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

    public static SmartFinder CreateDefault(HttpClient http) => new(
    [
        new StoreLinksProvider(),
        new SteamProvider(http),
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
    public async Task<FinderResults> SearchAsync(string text, CancellationToken ct = default, string? region = null,
        Action<FinderResults>? onUpdate = null, FinderIntent? category = null)
    {
        text = text.Trim();
        var query = new FinderQuery(text, category ?? DetectIntent(text), region ?? CurrentRegion()) { Terms = CoreTerms(text) };
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

        results.FailedSources.AddRange(answers.Select((a, i) => (a, i)).Where(x => x.a.Failed).Select(x => _providers[x.i].Name));
        results.PendingSources.AddRange(answers.Select((a, i) => (a, i)).Where(x => x.a.Items is null).Select(x => _providers[x.i].Name));

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
        _ => true,
    };
}
