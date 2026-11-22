namespace Bdm.Core.Finder;

/// <summary>Where a result is shown. Buy/rent comes first, then free legal sources, then legal torrents.</summary>
public enum FinderSection
{
    BuyOrRent,
    FreeLegal,
    LegalTorrent,
}

public enum FinderKind
{
    Game,
    Course,
    Video,
    Book,
    Audio,
    Software,
    Other,
}

/// <summary>What the user seems to be looking for; used to rank and pick sources.</summary>
public enum FinderIntent
{
    Any,
    Game,
    Course,
    Book,
}

public sealed record FinderQuery(string Text, FinderIntent Intent, string Region)
{
    /// <summary>The query without words like "pdf", "game" or "course" that confuse store and archive searches.</summary>
    public string Terms { get; init; } = Text;
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

    /// <summary>True for "search this store for your query" links (no specific item).</summary>
    public bool IsSearchLink { get; init; }
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

    public IEnumerable<FinderResult> In(FinderSection section) => Items.Where(i => i.Section == section);
}

/// <summary>A place Smart Finder searches.</summary>
public interface IFinderProvider
{
    string Name { get; }

    Task<IReadOnlyList<FinderResult>> SearchAsync(FinderQuery query, CancellationToken ct);
}
