using Bdm.App.Services;
using Bdm.Core;
using Bdm.Core.Finder;

namespace Bdm.App.Views;

/// <summary>
/// Smart Finder: search once, see where to buy or rent it, where it's free and legal, and official torrents.
/// Downloadable results open straight in BDM's Add page.
/// </summary>
public partial class FinderPage : ContentPage
{
    private readonly SmartFinder _finder;
    private CancellationTokenSource? _cts;

    public FinderPage(DownloadManager manager)
    {
        InitializeComponent();
        _finder = SmartFinder.CreateDefault(manager.Http);
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
            var final = await _finder.SearchAsync(text, cts.Token, onUpdate: partial =>
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
        BusyRow.IsVisible = !r.IsComplete;
        BusyLabel.Text = r.PendingSources.Count > 0 ? "Still searching " + string.Join(", ", r.PendingSources) + "…" : "";
        ResultsHost.Children.Clear();

        foreach (var notice in r.Notices)
            ResultsHost.Children.Add(Banner(notice, "#22F59E0B"));

        AddSection(r, FinderSection.BuyOrRent, "🛒  Buy or rent", "Official stores and platforms", "#2563EB");
        AddSection(r, FinderSection.FreeLegal, "🆓  Free and legal", "Official free uploads, public-domain and openly licensed works", "#16A34A");
        AddSection(r, FinderSection.LegalTorrent, "🧲  Legal torrents", "Official torrents of openly licensed and public-domain items", "#7C3AED");

        if (r.IsComplete && r.FailedSources.Count > 0)
            ResultsHost.Children.Add(new Label
            {
                Text = "Not reachable right now: " + string.Join(", ", r.FailedSources) + ".",
                Style = (Style)Application.Current!.Resources["Muted"], FontSize = 12,
            });
    }

    private void AddSection(FinderResults r, FinderSection section, string title, string subtitle, string color)
    {
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
        if (item.DownloadUrl is { } dl)
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
