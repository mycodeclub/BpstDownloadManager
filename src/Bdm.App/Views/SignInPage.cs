using Bdm.Core;
using Bdm.Core.Media;

namespace Bdm.App.Views;

/// <summary>
/// Sign in to YouTube inside BDM (for age-restricted, private and members-only videos). The password is
/// typed into Google's own page; BDM only keeps the resulting YouTube session cookies, in its private folder.
/// </summary>
public sealed class SignInPage : ContentPage
{
    private const string SignInUrl = "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F";

    private readonly DownloadManager _manager;
    private readonly Action? _onSignedIn;
    private readonly WebView _web = new() { Source = SignInUrl };
    private readonly Label _status = new() { Text = "Sign in with your Google account. Your password goes only to Google.", LineBreakMode = LineBreakMode.WordWrap, Margin = new Thickness(12, 8) };
    private bool _saved;

    public SignInPage(DownloadManager manager, Action? onSignedIn = null)
    {
        _manager = manager;
        _onSignedIn = onSignedIn;
        Title = "Sign in to YouTube";
        var grid = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)] };
        grid.Add(_status);
        grid.Add(_web, 0, 1);
        Content = grid;
        _web.Navigated += async (_, e) => await CheckAsync(e.Url);
    }

    private async Task CheckAsync(string? url)
    {
        if (_saved || url is null || !url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)) return;
        var cookies = await ReadCookiesAsync();
        if (!cookies.Any(c => c.Name == "LOGIN_INFO"))
        {
            _status.Text = "Not signed in to YouTube yet. Finish signing in.";
            return;
        }
        CookieFile.Write(_manager.YouTubeCookiesPath, cookies);
        _manager.Settings.CookiesFile = _manager.YouTubeCookiesPath;
        _manager.Settings.CookiesFromBrowser = null;
        _manager.SaveSettings();
        _saved = true;
        _status.Text = "Signed in. You can go back now.";
        _onSignedIn?.Invoke();
        await Navigation.PopAsync();
    }

    private async Task<List<CookieEntry>> ReadCookiesAsync()
    {
        var all = new Dictionary<(string, string), CookieEntry>();
#if WINDOWS
        if (_web.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 { CoreWebView2: { } core })
        {
            foreach (var origin in new[] { "https://www.youtube.com", "https://accounts.google.com", "https://www.google.com" })
                foreach (var c in await core.CookieManager.GetCookiesAsync(origin))
                    all[(c.Domain, c.Name)] = new CookieEntry(c.Domain, c.Path, c.Name, c.Value, c.IsSecure, c.IsHttpOnly,
                        c.IsSession ? null : DateTimeOffset.FromUnixTimeMilliseconds((long)(c.Expires * 1000)).UtcDateTime);
        }
#elif ANDROID
        await Task.CompletedTask;
        var cm = Android.Webkit.CookieManager.Instance;
        foreach (var (origin, domain) in new[] { ("https://www.youtube.com", ".youtube.com"), ("https://accounts.google.com", ".google.com") })
        {
            var header = cm?.GetCookie(origin);
            if (string.IsNullOrEmpty(header)) continue;
            foreach (var part in header.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var name = part[..eq];
                all[(domain, name)] = new CookieEntry(domain, "/", name, part[(eq + 1)..], true, false, DateTime.UtcNow.AddMonths(6));
            }
        }
#else
        await Task.CompletedTask;
#endif
        return all.Values.ToList();
    }

    /// <summary>Deletes the saved session.</summary>
    public static void SignOut(DownloadManager manager)
    {
        try { File.Delete(manager.YouTubeCookiesPath); } catch (IOException) { }
        if (string.Equals(manager.Settings.CookiesFile, manager.YouTubeCookiesPath, StringComparison.OrdinalIgnoreCase))
            manager.Settings.CookiesFile = null;
        manager.SaveSettings();
#if ANDROID
        Android.Webkit.CookieManager.Instance?.RemoveAllCookies(null);
#endif
    }
}
