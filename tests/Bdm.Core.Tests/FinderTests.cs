using System.Text.Json;
using Bdm.Core.Finder;

namespace Bdm.Core.Tests;

public class FinderTests
{
    [Theory]
    [InlineData("GTA 6", FinderIntent.Game)]
    [InlineData("mosh hamedani angular course", FinderIntent.Course)]
    [InlineData("data structures lectures", FinderIntent.Course)]
    [InlineData("clean code pdf", FinderIntent.Book)]
    [InlineData("sherlock holmes ebook", FinderIntent.Book)]
    [InlineData("big buck bunny", FinderIntent.Any)]
    public void Detects_what_the_user_is_looking_for(string text, FinderIntent intent) =>
        Assert.Equal(intent, SmartFinder.DetectIntent(text));

    [Theory]
    [InlineData("GTA 6 game", "GTA 6")]
    [InlineData("clean code pdf free download", "clean code")]
    [InlineData("mosh hamedani last video on angular", "mosh hamedani angular")]
    [InlineData("free pdf", "free pdf")]
    public void Strips_noise_words_for_store_searches(string text, string terms) => Assert.Equal(terms, SmartFinder.CoreTerms(text));

    [Theory]
    [InlineData("https://thepiratebay.org/search/gta", true)]
    [InlineData("https://www.1337x.to/torrent/1", true)]
    [InlineData("https://1337x2.to/x", true)]
    [InlineData("https://fitgirl-repacks.site/gta", true)]
    [InlineData("https://libgen.is/book", true)]
    [InlineData("https://z-library.sk/book/1", true)]
    [InlineData("https://annas-archive.org/md5/1", true)]
    [InlineData("https://archive.org/details/x", false)]
    [InlineData("https://store.steampowered.com/app/1/", false)]
    [InlineData("https://www.youtube.com/watch?v=x", false)]
    [InlineData("https://ytsomething.com/", false)]
    [InlineData("not a url", false)]
    public void Blocks_piracy_sites_only(string url, bool blocked) => Assert.Equal(blocked, SourceGuard.IsBlocked(url));

    [Theory]
    [InlineData("gta 6 LEAK!!! download FAST!!!", true)]
    [InlineData("Gta 6 Free Download Full Version PC", true)]
    [InlineData("Photoshop 2025 Crack + Keygen", true)]
    [InlineData("Grand Theft Auto VI Trailer 2", false)]
    [InlineData("Angular Tutorial for Beginners", false)]
    [InlineData("Fixing a memory leak in Angular", false)]
    [InlineData("OpenAI Codex crash course", false)]
    [InlineData("The Adventures of Sherlock Holmes", false)]
    public void Spots_pirate_titles(string title, bool pirated) => Assert.Equal(pirated, SourceGuard.LooksPirated(title));

    [Fact]
    public async Task Internet_archive_is_not_searched_for_games()
    {
        var r = await new InternetArchiveProvider(new HttpClient(new FailingHandler())).SearchAsync(new FinderQuery("GTA 6 game", FinderIntent.Game, "IN"), default);
        Assert.Empty(r);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("No network call expected");
    }

    [Fact]
    public void Steam_paid_games_go_to_buy_and_free_ones_to_free()
    {
        using var doc = JsonDocument.Parse("""
            {"items":[
              {"type":"app","name":"Grand Theft Auto V Enhanced","id":3240220,"price":{"currency":"INR","initial":249900,"final":249900},"tiny_image":"https://x/a.jpg"},
              {"type":"app","name":"Free Game","id":42,"tiny_image":"https://x/b.jpg"}
            ]}
            """);
        var r = SteamProvider.Parse(doc.RootElement);
        Assert.Equal(FinderSection.BuyOrRent, r[0].Section);
        Assert.Equal("INR 2,499.00", r[0].Price);
        Assert.Equal("https://store.steampowered.com/app/3240220/", r[0].Url);
        Assert.Equal(FinderSection.FreeLegal, r[1].Section);
        Assert.Equal("Free", r[1].Price);
    }

    [Fact]
    public void Internet_archive_items_get_their_licence_and_an_official_torrent()
    {
        using var doc = JsonDocument.Parse("""
            {"response":{"docs":[
              {"identifier":"caligari","title":"The Cabinet of Dr. Caligari","mediatype":"movies","licenseurl":"http://creativecommons.org/licenses/publicdomain/"},
              {"identifier":"talk","title":"A talk","mediatype":"audio","licenseurl":"http://creativecommons.org/licenses/by-nc-nd/2.5/"},
              {"identifier":"oldbook","title":"An old book","mediatype":"texts"}
            ]}}
            """);
        var r = InternetArchiveProvider.Parse(doc.RootElement);
        Assert.Equal(6, r.Count);
        Assert.Equal("Public domain", r[0].License);
        Assert.Equal(FinderKind.Video, r[0].Kind);
        Assert.Equal(FinderSection.LegalTorrent, r[1].Section);
        Assert.Equal("https://archive.org/download/caligari/caligari_archive.torrent", r[1].DownloadUrl);
        Assert.Equal("CC BY-NC-ND 2.5", r[2].License);
        Assert.Equal("Public domain", r[4].License);
    }

    [Fact]
    public void Gutenberg_books_download_as_epub()
    {
        using var doc = JsonDocument.Parse("""
            {"results":[{"id":1661,"title":"The Adventures of Sherlock Holmes","authors":[{"name":"Doyle, Arthur Conan"}],
              "formats":{"application/epub+zip":"https://www.gutenberg.org/ebooks/1661.epub3.images","image/jpeg":"https://x/c.jpg"}}]}
            """);
        var b = Assert.Single(GutenbergProvider.Parse(doc.RootElement));
        Assert.Equal("Public domain", b.License);
        Assert.Equal("https://www.gutenberg.org/ebooks/1661.epub3.images", b.DownloadUrl);
        Assert.Equal("Doyle, Arthur Conan", b.Detail);
    }

    [Fact]
    public async Task Buy_comes_first_piracy_is_dropped_and_failures_are_reported()
    {
        var finder = new SmartFinder(
        [
            new FakeProvider("Free", [new(FinderSection.FreeLegal, FinderKind.Video, "Trailer", "https://www.youtube.com/watch?v=t", "YouTube")]),
            new FakeProvider("Pirate", [new(FinderSection.LegalTorrent, FinderKind.Game, "GTA V", "https://1337x.to/t/1", "1337x")]),
            new FakeProvider("Archive", [new(FinderSection.FreeLegal, FinderKind.Software, "gta 6 LEAK!!! download FAST!!!", "https://archive.org/details/x", "Internet Archive")]),
            new FakeProvider("Store", [new(FinderSection.BuyOrRent, FinderKind.Game, "GTA V", "https://store.steampowered.com/app/1/", "Steam")]),
            new FakeProvider("Broken", null),
        ]);

        var r = await finder.SearchAsync("gta game", region: "IN");

        Assert.Equal(FinderIntent.Game, r.Intent);
        Assert.Equal(["GTA V", "Trailer"], r.Items.Select(i => i.Title));
        Assert.Equal(["Broken"], r.FailedSources);
    }

    [Fact]
    public async Task Says_so_when_there_is_no_legal_free_copy()
    {
        var finder = new SmartFinder(
        [
            new FakeProvider("Store", [new(FinderSection.BuyOrRent, FinderKind.Game, "GTA V", "https://store.steampowered.com/app/1/", "Steam")]),
            new FakeProvider("YouTube", [new(FinderSection.FreeLegal, FinderKind.Video, "GTA V trailer", "https://www.youtube.com/watch?v=t", "YouTube")]),
        ]);
        var r = await finder.SearchAsync("gta game", region: "IN");
        Assert.Contains(r.Notices, n => n.Contains("No legal free download"));
    }

    [Fact]
    public async Task Store_links_match_the_intent()
    {
        var links = await new StoreLinksProvider().SearchAsync(new FinderQuery("angular course", FinderIntent.Course, "IN"), default);
        Assert.Contains(links, l => l.Source == "Udemy");
        Assert.Contains(links, l => l.Source == "freeCodeCamp");
        Assert.DoesNotContain(links, l => l.Source == "Steam" || l.Kind == FinderKind.Game);
        Assert.All(links, l => Assert.True(l.IsSearchLink));
    }

    private sealed class FakeProvider(string name, IReadOnlyList<FinderResult>? items) : IFinderProvider
    {
        public string Name => name;

        public Task<IReadOnlyList<FinderResult>> SearchAsync(FinderQuery query, CancellationToken ct) =>
            items is null ? throw new HttpRequestException("down") : Task.FromResult(items);
    }
}
