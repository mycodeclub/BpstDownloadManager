using Bdm.App.Services;
using Bdm.Core;
using Bdm.Core.Finder;

namespace Bdm.App.Views;

/// <summary>
/// Smart Finder: search once for download links of games, software, e-books, documents and other files: where to
/// buy it, free legal downloads (open-source release files, free games, public-domain files) and official torrents.
/// Downloadable results open straight in BDM's Add page.
/// </summary>
public partial class FinderPage : ContentPage
{
    private readonly SmartFinder _finder;
    private readonly HttpClient _http;
    private CancellationTokenSource? _cts;
    private FinderIntent? _category;           // null = "All": guess the category from the words
    private FinderPrice _price = FinderPrice.Any;
    private FinderResults? _last;

    public FinderPage(DownloadManager manager)
    {
        InitializeComponent();
        _http = manager.Http;
        _finder = SmartFinder.CreateDefault(manager.Http);
        BuildChips();
    }

    private void BuildChips()
    {
        CategoryChips.Children.Clear();
        foreach (var (intent, label) in FinderFilters.Categories)
        {
            var chip = Chip(label, intent == _category);
            ToolTipProperties.SetText(chip, intent is null ? "Guess the category from your words" : intent == FinderIntent.Any ? "Search without a category" : null);
            chip.Clicked += (_, _) =>
            {
                if (_category == intent) return;
                _category = intent;
                BuildChips();
                if (!string.IsNullOrWhiteSpace(QueryEntry.Text)) Search_Clicked(null, EventArgs.Empty);
            };
            CategoryChips.Children.Add(chip);
        }
        PriceChips.Children.Clear();
        foreach (var (price, label) in FinderFilters.Prices)
        {
            var chip = Chip(label, price == _price);
            chip.Clicked += (_, _) =>
            {
                if (_price == price) return;
                _price = price;
                BuildChips();
                if (_last is not null) Show(_last);
            };
            PriceChips.Children.Add(chip);
        }
    }

    private static Button Chip(string text, bool selected) => new()
    {
        Text = text,
        Style = (Style)Application.Current!.Resources["SmallButton"],
        BackgroundColor = selected ? (Color)Application.Current!.Resources["Primary"] : null,
        TextColor = selected ? Colors.White : null,
        FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None,
        Margin = new Thickness(0, 0, 6, 6),
    };

    /// <summary>Picks a category chip (used by the --find= command-line switch for testing).</summary>
    public void SetCategory(FinderIntent? category)
    {
        _category = category;
        BuildChips();
    }

    /// <summary>Runs a search (used by the --find= command-line switch and the Search button).</summary>
    public void Search(string text)
    {
        QueryEntry.Text = text;
        Search_Clicked(null, EventArgs.Empty);
    }

    private async void Search_Clicked(object? sender, EventArgs e)
    {
        var text = QueryEntry.Text?.Trim() ?? "";
        if (text.Length == 0) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        QueryEntry.Unfocus();
        ResultsHost.Children.Clear();
        BusyLabel.Text = "Searching " + string.Join(", ", _finder.SourceNames) + "…";
        BusyRow.IsVisible = true;
        try
        {
            var final = await _finder.SearchAsync(text, cts.Token, category: _category, onUpdate: partial =>
                MainThread.BeginInvokeOnMainThread(() => { if (!cts.IsCancellationRequested) Show(partial); }));
            if (!cts.IsCancellationRequested) Show(final);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Error("Smart Finder failed", ex);
            ResultsHost.Children.Clear();
            ResultsHost.Children.Add(Banner("Search failed: " + ex.Message, "#33DC2626"));
            BusyRow.IsVisible = false;
        }
    }

    private void Show(FinderResults r)
    {
        _last = r;
        BusyRow.IsVisible = !r.IsComplete;
        BusyLabel.Text = r.PendingSources.Count > 0 ? "Still searching " + string.Join(", ", r.PendingSources) + "…" : "";
        ResultsHost.Children.Clear();

        if (_price != FinderPrice.Paid)
            foreach (var notice in r.Notices)
                ResultsHost.Children.Add(Banner(notice, "#22F59E0B"));
        if (r.IsComplete && !r.Items.Any(i => _price.Allows(i.Section)))
            ResultsHost.Children.Add(Banner(_price == FinderPrice.Paid ? "No paid sources found for this." : "Nothing found. Try other words or another category.", "#22F59E0B"));

        AddSection(r, FinderSection.BuyOrRent, "🛒  Buy", "Official stores and platforms", "#2563EB");
        AddSection(r, FinderSection.FreeLegal, "🆓  Free downloads", "Open-source release files, free games, public-domain and openly licensed files", "#16A34A");
        AddSection(r, FinderSection.LegalTorrent, "🧲  Official torrents", "Torrents published by the makers themselves", "#7C3AED");

        if (r.IsComplete && r.FailedSources.Count > 0)
            ResultsHost.Children.Add(new Label
            {
                Text = "Not reachable right now: " + string.Join(", ", r.FailedSources) + ".",
                Style = (Style)Application.Current!.Resources["Muted"], FontSize = 12,
            });
    }

    private void AddSection(FinderResults r, FinderSection section, string title, string subtitle, string color)
    {
        if (!_price.Allows(section)) return;
        var items = r.In(section).ToList();
        if (items.Count == 0) return;
        var stack = new VerticalStackLayout { Spacing = 8 };
        stack.Add(new Label { Text = title, Style = (Style)Application.Current!.Resources["SectionTitle"] });
        stack.Add(new Label { Text = subtitle, Style = (Style)Application.Current!.Resources["Muted"], FontSize = 12 });

        var specific = items.Where(i => !i.IsSearchLink).ToList();
        foreach (var item in specific)
            stack.Add(ResultRow(item, color));

        var links = items.Where(i => i.IsSearchLink).ToList();
        if (links.Count > 0)
        {
            if (specific.Count > 0) stack.Add(new Label { Text = "Search on:", FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 6, 0, 0) });
            var flex = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            foreach (var link in links)
            {
                var b = new Button
                {
                    Text = link.IsSearchLink && link.Title.StartsWith("Search", StringComparison.Ordinal) ? link.Source : link.Title,
                    Style = (Style)Application.Current!.Resources["SmallButton"], Margin = new Thickness(0, 0, 6, 6),
                };
                ToolTipProperties.SetText(b, link.Detail ?? link.Url);
                b.Clicked += async (_, _) => await OpenAsync(link.Url);
                flex.Add(b);
            }
            stack.Add(flex);
        }

        ResultsHost.Children.Add(new Border { Style = (Style)Application.Current!.Resources["Card"], Content = stack });
    }

    private View ResultRow(FinderResult item, string color)
    {
        var grid = new Grid
        {
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            ColumnSpacing = 12, Padding = new Thickness(0, 6),
        };
        if (item.ThumbnailUrl is { } thumb)
            grid.Add(new Image { Source = ImageSource.FromUri(new Uri(thumb)), WidthRequest = 96, HeightRequest = 54, Aspect = Aspect.AspectFill, VerticalOptions = LayoutOptions.Start });
        else
            grid.Add(new BoxView { WidthRequest = 96, HeightRequest = 0 });

        var text = new VerticalStackLayout { Spacing = 2 };
        text.Add(new Label { Text = item.Title, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 });
        var meta = string.Join(" · ", new[] { item.Source, item.Detail }.Where(s => !string.IsNullOrWhiteSpace(s)));
        text.Add(new Label { Text = meta, Style = (Style)Application.Current!.Resources["Muted"], FontSize = 12, LineBreakMode = LineBreakMode.TailTruncation });
        var tags = new HorizontalStackLayout { Spacing = 6 };
        if (item.Price is { } price) tags.Add(Tag(price, color));
        if (item.License is { } lic) tags.Add(Tag(lic, "#64748B"));
        if (tags.Children.Count > 0) text.Add(tags);
        grid.Add(text, 1);

        var buttons = new VerticalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        if (item.ReleaseFilesUrl is { } release)
        {
            var files = new Button { Text = "⬇ Download", Style = (Style)Application.Current!.Resources["PrimaryButton"] };
            files.Clicked += async (_, _) => await PickReleaseFileAsync(item, release);
            buttons.Add(files);
        }
        else if (item.DownloadUrl is { } dl)
        {
            var download = new Button { Text = "⬇ Download", Style = (Style)Application.Current!.Resources["PrimaryButton"] };
            download.Clicked += async (_, _) => await AppNavigation.OpenAddAsync(dl);
            buttons.Add(download);
        }
        var open = new Button { Text = item.Section == FinderSection.BuyOrRent ? "View in store" : "Open", Style = (Style)Application.Current!.Resources["SmallButton"] };
        open.Clicked += async (_, _) => await OpenAsync(item.Url);
        buttons.Add(open);
        grid.Add(buttons, 2);
        return grid;
    }

    /// <summary>Lists the files of the project's latest release and sends the chosen one to the Add page.</summary>
    private async Task PickReleaseFileAsync(FinderResult item, string releaseApiUrl)
    {
        IReadOnlyList<FinderFile> files;
        try { files = await GitHubProvider.GetReleaseFilesAsync(_http, releaseApiUrl); }
        catch (Exception ex)
        {
            AppLog.Error("Couldn't list release files", ex);
            files = [];
        }
        if (files.Count == 0)
        {
            await OpenAsync(item.Url); // no downloadable files: show the releases page
            return;
        }
        var labels = files.Select(f => $"{f.Name}  ({Format.Bytes(f.Size)})").ToArray();
        var choice = await DisplayActionSheetAsync($"Download {item.Title}: pick a file", "Cancel", null, labels);
        var index = Array.IndexOf(labels, choice);
        if (index >= 0) await AppNavigation.OpenAddAsync(files[index].Url);
    }

    private static Border Tag(string text, string color) => new()
    {
        Style = (Style)Application.Current!.Resources["Badge"],
        BackgroundColor = Color.FromArgb(color),
        Content = new Label { Text = text, TextColor = Colors.White, FontSize = 11 },
    };

    private static Border Banner(string text, string color) => new()
    {
        Style = (Style)Application.Current!.Resources["Banner"],
        BackgroundColor = Color.FromArgb(color),
        Content = new Label { Text = text, LineBreakMode = LineBreakMode.WordWrap },
    };

    private static async Task OpenAsync(string url)
    {
        try { await Launcher.Default.OpenAsync(new Uri(url)); }
        catch (Exception ex) { AppLog.Error("Couldn't open " + url, ex); }
    }
}
