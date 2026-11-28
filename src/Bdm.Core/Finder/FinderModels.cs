namespace Bdm.Core.Finder;

/// <summary>
/// Where a result is shown: streaming services (subscription) first for movies and series, then buy/rent,
/// then free legal sources, then legal torrents.
/// </summary>
public enum FinderSection
{
    Watch,
    BuyOrRent,
    FreeLegal,
    LegalTorrent,
}

public enum FinderKind
{
    Game,
    Software,
    Course,
    Book,
    Document,
    Audio,
    Movie,
    Series,
    Other,
}

/// <summary>What the user is looking for (picked as a category, or guessed from the words); picks and ranks sources.</summary>
public enum FinderIntent
{
    Any,
    Game,
    Software,
    Course,
    Book,
    Document,
    Audio,
    Movie,
    Series,
}

/// <summary>Free/paid filter, applied to results already found.</summary>
public enum FinderPrice
{
    Any,
    Free,
    Paid,
}

public static class FinderFilters
{
    /// <summary>
    /// Category chips. <c>null</c> = "All" (BDM guesses the category from the words);
    /// <see cref="FinderIntent.Any"/> = "Other" (no category, every general source).
    /// </summary>
    public static IReadOnlyList<(FinderIntent? Intent, string Label)> Categories { get; } =
    [
        (null, "All"),
        (FinderIntent.Game, "🎮 Games"),
        (FinderIntent.Software, "💻 Software"),
        (FinderIntent.Course, "🎓 Courses"),
        (FinderIntent.Book, "📚 E-books & PDF"),
        (FinderIntent.Document, "📄 Documents"),
        (FinderIntent.Audio, "🎵 Music & audio"),
        (FinderIntent.Movie, "🎬 Movies"),
        (FinderIntent.Series, "📺 Web series & TV"),
        (FinderIntent.Any, "Other"),
    ];

    /// <summary>Language chips for movies and series. <c>null</c> = any language. Codes are ISO 639-1.</summary>
    public static IReadOnlyList<(string? Code, string Label)> Languages { get; } =
    [
        (null, "Any language"), ("hi", "Hindi"), ("en", "English"), ("ta", "Tamil"), ("te", "Telugu"), ("ml", "Malayalam"),
        ("kn", "Kannada"), ("bn", "Bengali"), ("mr", "Marathi"), ("pa", "Punjabi"), ("gu", "Gujarati"),
        ("ko", "Korean"), ("ja", "Japanese"), ("es", "Spanish"), ("fr", "French"),
    ];

    /// <summary>Language name for an ISO 639-1 code ("hi" → "Hindi"), or the code itself if it isn't in the list.</summary>
    public static string LanguageName(string? code) =>
        code is null ? "" : Languages.FirstOrDefault(l => l.Code == code).Label ?? code;

    public static bool IsWatchIntent(this FinderIntent intent) => intent is FinderIntent.Movie or FinderIntent.Series;

    public static IReadOnlyList<(FinderPrice Price, string Label)> Prices { get; } =
        [(FinderPrice.Any, "Free + paid"), (FinderPrice.Free, "Free only"), (FinderPrice.Paid, "Paid only")];

    public static bool Allows(this FinderPrice price, FinderSection section) => price switch
    {
        FinderPrice.Free => section is FinderSection.FreeLegal or FinderSection.LegalTorrent,
        FinderPrice.Paid => section is FinderSection.BuyOrRent or FinderSection.Watch,
        _ => true,
    };
}

public sealed record FinderQuery(string Text, FinderIntent Intent, string Region)
{
    /// <summary>The query without words like "pdf", "game" or "course" that confuse store and archive searches.</summary>
    public string Terms { get; init; } = Text;

    /// <summary>Wanted language for movies and series (ISO 639-1, e.g. "hi"), or null for any.</summary>
    public string? Language { get; init; }
}

public sealed record FinderResult(FinderSection Section, FinderKind Kind, string Title, string Url, string Source)
{
    /// <summary>Short extra line: channel and duration, author, platform…</summary>
    public string? Detail { get; init; }

    /// <summary>Price as shown by the store (e.g. "INR 2,499.00"), or "Free".</summary>
    public string? Price { get; init; }

    /// <summary>Why it's legal to download for free (e.g. "Public domain", "CC BY 4.0", "Official upload").</summary>
    public string? License { get; init; }

    public string? ThumbnailUrl { get; init; }

    /// <summary>A link BDM can download directly (video page, file or .torrent). Null = open in the browser only.</summary>
    public string? DownloadUrl { get; init; }

    /// <summary>GitHub API address of the project's latest release; its files are listed when Download is pressed.</summary>
    public string? ReleaseFilesUrl { get; init; }

    /// <summary>True for "search this store for your query" links (no specific item).</summary>
    public bool IsSearchLink { get; init; }

    /// <summary>Data credit the app must show with this result (e.g. "Streaming data: JustWatch, via TMDB").</summary>
    public string? Credit { get; init; }
}

public sealed class FinderResults
{
    public required string Query { get; init; }
    public required FinderIntent Intent { get; init; }
    public List<FinderResult> Items { get; } = [];
    public List<string> Notices { get; } = [];
    public List<string> FailedSources { get; } = [];

    /// <summary>Sources still searching (only in progress updates).</summary>
    public List<string> PendingSources { get; } = [];

    public bool IsComplete { get; init; } = true;

    /// <summary>Data credits to show under the results (from <see cref="FinderResult.Credit"/>).</summary>
    public List<string> Credits { get; } = [];

    public IEnumerable<FinderResult> In(FinderSection section) => Items.Where(i => i.Section == section);
}

/// <summary>A place Smart Finder searches.</summary>
public interface IFinderProvider
{
    string Name { get; }

    Task<IReadOnlyList<FinderResult>> SearchAsync(FinderQuery query, CancellationToken ct);
}

/// <summary>A downloadable file of a software release.</summary>
public sealed record FinderFile(string Name, long Size, string Url);
