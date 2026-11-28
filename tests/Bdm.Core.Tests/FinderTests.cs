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
    [InlineData("video editor for pc", FinderIntent.Software)]
    [InlineData("ubuntu iso", FinderIntent.Software)]
    [InlineData("attention is all you need research paper", FinderIntent.Document)]
    [InlineData("arijit singh songs", FinderIntent.Audio)]
    public void Detects_what_the_user_is_looking_for(string text, FinderIntent intent) =>
        Assert.Equal(intent, SmartFinder.DetectIntent(text));

    [Theory]
    [InlineData("GTA 6 game", "GTA 6")]
    [InlineData("clean code pdf free download", "clean code")]
    [InlineData("mosh hamedani last video on angular", "mosh hamedani video angular")]
    [InlineData("free pdf", "free pdf")]
    [InlineData("Mirzapur web series", "Mirzapur")]
    [InlineData("video editor software", "video editor")]
    [InlineData("3 idiots full movie hd", "3 idiots")]
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
    [InlineData("https://ytsrs.bz/movies/die-hard", true)]
    [InlineData("https://torgamez.org/game/1", true)]
    [InlineData("https://www.movierulz.vpn/x", true)]
    [InlineData("https://vegamovies3.tv/x", true)]
    [InlineData("https://www.netflix.com/search?q=x", false)]
    [InlineData("https://www.hotstar.com/in", false)]
    [InlineData("https://www.justwatch.com/in/search?q=x", false)]
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
        Assert.Equal(FinderKind.Course, r[0].Kind);
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
            new FakeProvider("Free", [new(FinderSection.FreeLegal, FinderKind.Other, "Trailer", "https://www.youtube.com/watch?v=t", "YouTube")]),
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
            new FakeProvider("YouTube", [new(FinderSection.FreeLegal, FinderKind.Other, "GTA V trailer", "https://www.youtube.com/watch?v=t", "YouTube")]),
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

    [Theory]
    [InlineData(FinderPrice.Any, FinderSection.BuyOrRent, true)]
    [InlineData(FinderPrice.Free, FinderSection.BuyOrRent, false)]
    [InlineData(FinderPrice.Free, FinderSection.FreeLegal, true)]
    [InlineData(FinderPrice.Free, FinderSection.LegalTorrent, true)]
    [InlineData(FinderPrice.Paid, FinderSection.BuyOrRent, true)]
    [InlineData(FinderPrice.Paid, FinderSection.FreeLegal, false)]
    public void Free_and_paid_filter(FinderPrice price, FinderSection section, bool shown) => Assert.Equal(shown, price.Allows(section));

    [Fact]
    public void Every_category_has_a_chip_plus_all_and_other()
    {
        var intents = FinderFilters.Categories.Select(c => c.Intent).ToList();
        Assert.Null(intents[0]);
        Assert.Equal(FinderIntent.Any, intents[^1]);
        foreach (var i in Enum.GetValues<FinderIntent>()) Assert.Contains(i, intents);
    }

    [Fact]
    public async Task Picked_category_overrides_the_guess()
    {
        var seen = FinderIntent.Any;
        var finder = new SmartFinder([new SpyProvider(q => seen = q.Intent)]);
        await finder.SearchAsync("gta", category: FinderIntent.Software);
        Assert.Equal(FinderIntent.Software, seen);
        await finder.SearchAsync("gta game");
        Assert.Equal(FinderIntent.Game, seen);
    }

    [Theory]
    [InlineData(FinderIntent.Software, "F-Droid")]
    [InlineData(FinderIntent.Game, "GOG free games")]
    [InlineData(FinderIntent.Document, "Google Scholar")]
    [InlineData(FinderIntent.Audio, "Free Music Archive")]
    public async Task Each_category_lists_its_official_sources(FinderIntent intent, string source)
    {
        var links = await new StoreLinksProvider().SearchAsync(new FinderQuery("x", intent, "IN"), default);
        Assert.Contains(links, l => l.Source == source);
        Assert.DoesNotContain(links, l => l.Kind == FinderKind.Game && intent != FinderIntent.Game);
    }

    [Theory]
    [InlineData(FinderIntent.Software)]
    public async Task Internet_archive_is_skipped_where_free_copies_are_not_trustworthy(FinderIntent intent)
    {
        var r = await new InternetArchiveProvider(new HttpClient(new FailingHandler())).SearchAsync(new FinderQuery("x", intent, "IN"), default);
        Assert.Empty(r);
    }

    [Fact]
    public void GitHub_projects_need_a_licence()
    {
        using var doc = JsonDocument.Parse("""
            {"items":[
              {"name":"lossless-cut","full_name":"mifi/lossless-cut","html_url":"https://github.com/mifi/lossless-cut","stargazers_count":46308,"description":"Video editing",
               "license":{"spdx_id":"GPL-2.0"},"owner":{"login":"mifi","avatar_url":"https://x/a.png"}},
              {"name":"nolicence","full_name":"a/b","html_url":"https://github.com/a/b","stargazers_count":5,"description":null,"license":null,"owner":{"login":"a","avatar_url":"https://x/b.png"}}
            ]}
            """);
        var r = Assert.Single(GitHubProvider.Parse(doc.RootElement));
        Assert.Equal("Open source (GPL-2.0)", r.License);
        Assert.Equal("https://github.com/mifi/lossless-cut/releases/latest", r.Url);
        Assert.Contains("★ 46,308", r.Detail);
    }

    [Fact]
    public void Arxiv_papers_download_as_pdf()
    {
        var r = Assert.Single(ArxivProvider.Parse("""
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <id>http://arxiv.org/abs/1706.03762v7</id>
                <title>Attention Is All
                  You Need</title>
                <published>2017-06-12T17:57:34Z</published>
                <link href="https://arxiv.org/abs/1706.03762v7" rel="alternate" type="text/html"/>
                <link href="https://arxiv.org/pdf/1706.03762v7" rel="related" type="application/pdf" title="pdf"/>
                <author><name>Ashish Vaswani</name></author><author><name>Noam Shazeer</name></author>
              </entry>
            </feed>
            """));
        Assert.Equal("Attention Is All You Need", r.Title);
        Assert.Equal("https://arxiv.org/pdf/1706.03762v7.pdf", r.DownloadUrl);
        Assert.Equal("Ashish Vaswani et al. · 2017", r.Detail);
        Assert.Equal(FinderKind.Document, r.Kind);
    }

    private sealed class SpyProvider(Action<FinderQuery> onSearch) : IFinderProvider
    {
        public string Name => "Spy";

        public Task<IReadOnlyList<FinderResult>> SearchAsync(FinderQuery query, CancellationToken ct)
        {
            onSearch(query);
            return Task.FromResult<IReadOnlyList<FinderResult>>([]);
        }
    }

    [Fact]
    public void GitHub_release_lists_real_files_only()
    {
        using var doc = JsonDocument.Parse("""
            {"tag_name":"v3.70.2","assets":[
              {"name":"latest-mac.yml","size":500,"browser_download_url":"https://github.com/m/l/releases/download/v3.70.2/latest-mac.yml"},
              {"name":"LosslessCut-win-x64.7z","size":120000000,"browser_download_url":"https://github.com/m/l/releases/download/v3.70.2/LosslessCut-win-x64.7z"},
              {"name":"LosslessCut-linux-x64.tar.bz2","size":209203853,"browser_download_url":"https://github.com/m/l/releases/download/v3.70.2/LosslessCut-linux-x64.tar.bz2"},
              {"name":"licenses.txt","size":1296967,"browser_download_url":"https://github.com/m/l/releases/download/v3.70.2/licenses.txt"}
            ]}
            """);
        var files = GitHubProvider.ParseRelease(doc.RootElement);
        Assert.Equal(["LosslessCut-linux-x64.tar.bz2", "LosslessCut-win-x64.7z"], files.Select(f => f.Name));
    }

    [Fact]
    public void GitHub_results_point_at_the_latest_release()
    {
        using var doc = JsonDocument.Parse("""
            {"items":[{"name":"stuntrally","full_name":"stuntrally/stuntrally","html_url":"https://github.com/stuntrally/stuntrally","stargazers_count":649,
              "description":"Racing game","license":{"spdx_id":"GPL-3.0"},"owner":{"login":"stuntrally","avatar_url":"https://x/a.png"}}]}
            """);
        var r = Assert.Single(GitHubProvider.Parse(doc.RootElement, FinderKind.Game));
        Assert.Equal(FinderKind.Game, r.Kind);
        Assert.Equal("https://api.github.com/repos/stuntrally/stuntrally/releases/latest", r.ReleaseFilesUrl);
    }

    [Theory]
    [InlineData("ubuntu 24.04", "Ubuntu")]
    [InlineData("linux iso", "Debian")]
    [InlineData("photoshop", null)]
    public async Task Official_linux_torrents(string text, string? source)
    {
        var r = await new OfficialTorrentsProvider().SearchAsync(new FinderQuery(text, FinderIntent.Software, "IN"), default);
        if (source is null) Assert.Empty(r);
        else Assert.Contains(r, x => x.Source == source && x.Section == FinderSection.LegalTorrent);
    }

    [Fact]
    public void GitHub_results_must_mention_every_search_word()
    {
        using var doc = JsonDocument.Parse("""
            {"items":[
              {"name":"OpenCut","full_name":"OpenCut-app/OpenCut","html_url":"https://github.com/OpenCut-app/OpenCut","stargazers_count":99127,
               "description":"The open-source CapCut alternative video editor","license":{"spdx_id":"MIT"},"owner":{"login":"OpenCut-app","avatar_url":"https://x/a.png"}},
              {"name":"photocraft","full_name":"storytold/photocraft","html_url":"https://github.com/storytold/photocraft","stargazers_count":80952,
               "description":"An open-source photo editor","license":{"spdx_id":"MIT"},"owner":{"login":"storytold","avatar_url":"https://x/b.png"}}
            ]}
            """);
        Assert.Equal(["OpenCut"], GitHubProvider.Parse(doc.RootElement, FinderKind.Software, "video editor").Select(r => r.Title));
    }

    // ---- movies and web series

    [Theory]
    [InlineData("die hard 4 movie", FinderIntent.Movie)]
    [InlineData("Panchayat web series", FinderIntent.Series)]
    [InlineData("breaking bad season 3", FinderIntent.Series)]
    [InlineData("pushpa 2 hindi dubbed", FinderIntent.Movie)]
    [InlineData("hindi songs", FinderIntent.Audio)]
    public void Detects_movies_and_series(string text, FinderIntent intent) => Assert.Equal(intent, SmartFinder.DetectIntent(text));

    [Theory]
    [InlineData("die hard 4 in hindi", "hi")]
    [InlineData("KGF tamil dubbed", "ta")]
    [InlineData("die hard 4", null)]
    public void Detects_the_wanted_language(string text, string? code) => Assert.Equal(code, SmartFinder.DetectLanguage(text));

    [Theory]
    [InlineData("die hard 4 in hindi", "die hard 4")]
    [InlineData("Mirzapur season 2 web series", "Mirzapur")]
    [InlineData("pushpa 2 hindi dubbed full movie", "pushpa 2")]
    [InlineData("KGF hindi", "KGF")]
    [InlineData("Breaking Bad season 3 episode 2", "Breaking Bad")]
    [InlineData("Hindi Medium", "Hindi Medium")]
    [InlineData("English Vinglish", "English Vinglish")]
    [InlineData("The Truman Show", "The Truman Show")]
    [InlineData("Lost in Translation", "Lost in Translation")]
    public void Watch_searches_keep_only_the_title(string text, string terms) => Assert.Equal(terms, SmartFinder.WatchTerms(text));

    [Fact]
    public async Task A_language_word_alone_means_a_movie_search()
    {
        FinderQuery? seen = null;
        await new SmartFinder([new SpyProvider(q => seen = q)]).SearchAsync("die hard 4 in hindi", region: "IN");
        Assert.NotNull(seen);
        Assert.Equal(FinderIntent.Movie, seen!.Intent);
        Assert.Equal("hi", seen.Language);
        Assert.Equal("die hard 4", seen.Terms);
    }

    [Theory]
    [InlineData(FinderPrice.Paid, FinderSection.Watch, true)]
    [InlineData(FinderPrice.Free, FinderSection.Watch, false)]
    [InlineData(FinderPrice.Any, FinderSection.Watch, true)]
    public void Streaming_counts_as_paid(FinderPrice price, FinderSection section, bool shown) => Assert.Equal(shown, price.Allows(section));

    [Fact]
    public void Tmdb_search_puts_the_wanted_language_first_and_skips_people()
    {
        using var doc = JsonDocument.Parse("""
            {"results":[
              {"id":1,"media_type":"movie","title":"Drishyam","release_date":"2013-12-19","original_language":"ml","vote_count":900,"poster_path":"/a.jpg"},
              {"id":2,"media_type":"person","name":"Someone","vote_count":0},
              {"id":3,"media_type":"movie","title":"Drishyam","release_date":"2015-07-31","original_language":"hi","vote_count":700},
              {"id":4,"media_type":"tv","name":"Obscure show","first_air_date":"2020-01-01","original_language":"en","vote_count":3}
            ]}
            """);
        var titles = TmdbWatchProvider.ParseSearch(doc.RootElement, FinderIntent.Any, "hi");
        Assert.Equal([3L, 1L], titles.Select(t => t.Id));
        Assert.Equal("2015", titles[0].Year);
        Assert.Equal("movie", titles[0].MediaType);
    }

    [Fact]
    public void Tmdb_providers_become_watch_rent_buy_and_free_results()
    {
        using var doc = JsonDocument.Parse("""
            {"id":1571,"results":{
              "IN":{"link":"https://www.themoviedb.org/movie/1571-live-free-or-die-hard/watch?locale=IN",
                    "flatrate":[{"provider_id":2336,"provider_name":"JioHotstar"},{"provider_id":8,"provider_name":"Netflix"}],
                    "rent":[{"provider_id":3,"provider_name":"Google Play Movies"}],
                    "buy":[{"provider_id":3,"provider_name":"Google Play Movies"}],
                    "ads":[{"provider_id":9,"provider_name":"Amazon MX Player"}]},
              "US":{"link":"https://x","flatrate":[{"provider_id":15,"provider_name":"Hulu"}]}}}
            """);
        var t = new TmdbWatchProvider.Title(1571, "movie", "Live Free or Die Hard", "2007", "en", "/p.jpg");
        var r = TmdbWatchProvider.ParseProviders(doc.RootElement, "IN", t);
        Assert.DoesNotContain(r, x => x.Source == "Hulu");
        var hotstar = Assert.Single(r, x => x.Source == "JioHotstar");
        Assert.Equal(FinderSection.Watch, hotstar.Section);
        Assert.Equal("Live Free or Die Hard (2007)", hotstar.Title);
        Assert.StartsWith("https://www.themoviedb.org/movie/1571", hotstar.Url);
        Assert.Equal("https://www.netflix.com/search?q=Live%20Free%20or%20Die%20Hard", Assert.Single(r, x => x.Source == "Netflix").Url);
        Assert.Equal("Rent or buy", Assert.Single(r, x => x.Source == "Google Play Movies").Price);
        Assert.Equal(FinderSection.FreeLegal, Assert.Single(r, x => x.Source == "Amazon MX Player").Section);
        Assert.All(r, x => Assert.Equal(TmdbWatchProvider.CreditText, x.Credit));
        Assert.Equal(r.Count, r.Select(x => x.Section + x.Url).Distinct().Count()); // not merged away as duplicates
    }

    [Fact]
    public void Tmdb_title_not_streaming_in_the_country_says_so()
    {
        using var doc = JsonDocument.Parse("""{"id":1,"results":{}}""");
        var r = Assert.Single(TmdbWatchProvider.ParseProviders(doc.RootElement, "IN", new TmdbWatchProvider.Title(1, "tv", "Show", null, "en", null)));
        Assert.Equal(FinderKind.Series, r.Kind);
        Assert.Contains("Not on any streaming service", r.Detail);
    }

    [Fact]
    public async Task Without_a_tmdb_key_movie_searches_still_get_service_links()
    {
        var finder = new SmartFinder([new TmdbWatchProvider(new HttpClient(new FailingHandler()), () => null), new WatchLinksProvider()]);
        var r = await finder.SearchAsync("die hard 4 in hindi", region: "IN");
        Assert.Empty(r.FailedSources);
        Assert.Contains(r.Items, i => i.Source == "JustWatch" && i.Url == "https://www.justwatch.com/in/search?q=die%20hard%204");
        Assert.Contains(r.Items, i => i.Source == "JioHotstar");
        Assert.Contains(r.Notices, n => n.StartsWith("Hindi audio", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Watch_links_are_only_for_movies_and_series()
    {
        Assert.Empty(await new WatchLinksProvider().SearchAsync(new FinderQuery("gta", FinderIntent.Game, "IN"), default));
        var us = await new WatchLinksProvider().SearchAsync(new FinderQuery("x", FinderIntent.Series, "US"), default);
        Assert.DoesNotContain(us, l => l.Source == "JioHotstar");
        Assert.All(us, l => Assert.Equal(FinderKind.Series, l.Kind));
    }

    [Fact]
    public void Gog_games_with_prices_and_free_installers()
    {
        using var doc = JsonDocument.Parse("""
            {"products":[
              {"title":"Reigns: The Witcher","storeLink":"https://www.gog.com/en/game/reigns_the_witcher","developers":["Nerial"],
               "price":{"final":"$4.79","finalMoney":{"amount":"4.79","currency":"USD"}}},
              {"title":"Gwent","storeLink":"https://www.gog.com/en/game/gwent","price":{"finalMoney":{"amount":"0.00","currency":"USD"}}},
              {"title":"Coming soon","storeLink":"https://www.gog.com/en/game/soon","price":null}
            ]}
            """);
        var r = GogProvider.Parse(doc.RootElement);
        Assert.Equal(2, r.Count);
        Assert.Equal(FinderSection.BuyOrRent, r[0].Section);
        Assert.Equal("USD 4.79", r[0].Price);
        Assert.Equal(FinderSection.FreeLegal, r[1].Section);
        Assert.Equal("Free", r[1].Price);
    }

    [Fact]
    public void Archive_films_must_be_old_enough_or_openly_licensed()
    {
        using var doc = JsonDocument.Parse("""
            {"response":{"docs":[
              {"identifier":"nosferatu","title":"Nosferatu","mediatype":"movies","year":1922},
              {"identifier":"newfilm","title":"A 2007 action film","mediatype":"movies","year":"2007"},
              {"identifier":"ccfilm","title":"Big Buck Bunny","mediatype":"movies","year":"2008","licenseurl":"http://creativecommons.org/licenses/by/3.0/"}
            ]}}
            """);
        var r = InternetArchiveProvider.Parse(doc.RootElement, FinderIntent.Movie);
        Assert.Equal(["Nosferatu", "Big Buck Bunny"], r.Where(x => x.Section == FinderSection.FreeLegal).Select(x => x.Title));
        Assert.All(r, x => Assert.Equal(FinderKind.Movie, x.Kind));
    }

    [Theory]
    [InlineData("1930", 2026, true)]
    [InlineData("1931", 2026, false)]
    [InlineData("2007", 2026, false)]
    [InlineData(null, 2026, false)]
    [InlineData("1922-05-01", 2026, true)]
    public void Public_domain_by_age(string? year, int now, bool pd) => Assert.Equal(pd, InternetArchiveProvider.IsPublicDomainByAge(year, now));

    [Fact]
    public async Task More_legal_game_stores()
    {
        var links = await new StoreLinksProvider().SearchAsync(new FinderQuery("x", FinderIntent.Game, "IN"), default);
        foreach (var source in new[] { "GOG", "Humble Store", "Game Jolt", "IndieGala" })
            Assert.Contains(links, l => l.Source == source);
    }
}
