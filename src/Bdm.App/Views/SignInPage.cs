using Bdm.Core;
using Bdm.Core.Media;

namespace Bdm.App.Views;

/// <summary>A site the user can sign in to inside BDM (for age-restricted, private or login-only videos).</summary>
public sealed record SignInSite(string Name, string LoginUrl, string[] Origins, string Domain, string LoggedInCookie)
{
    public static readonly SignInSite YouTube = new("YouTube",
        "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F",
        ["https://www.youtube.com", "https://accounts.google.com", "https://www.google.com"], "youtube.com", "LOGIN_INFO");

    public static readonly SignInSite Instagram = new("Instagram", "https://www.instagram.com/accounts/login/",
        ["https://www.instagram.com"], "instagram.com", "sessionid");

    public static readonly SignInSite Facebook = new("Facebook", "https://www.facebook.com/login/",
        ["https://www.facebook.com"], "facebook.com", "c_user");

    public static readonly SignInSite[] All = [YouTube, Instagram, Facebook];

    /// <summary>The site a video link belongs to (for "Sign in to …" buttons), or YouTube by default.</summary>
    public static SignInSite For(string? url) =>
        url?.Contains("instagram.com", StringComparison.OrdinalIgnoreCase) == true ? Instagram
        : url?.Contains("facebook.com", StringComparison.OrdinalIgnoreCase) == true || url?.Contains("fb.watch", StringComparison.OrdinalIgnoreCase) == true ? Facebook
        : YouTube;
}

/// <summary>
/// Sign in to a video site inside BDM. The password is typed into the site's own login page; BDM only keeps
/// the resulting session cookies, in its private folder, merged with other sites' sign-ins.
/// </summary>
public sealed class SignInPage : ContentPage
{
    private readonly DownloadManager _manager;
    private readonly SignInSite _site;
    private readonly Action? _onSignedIn;
    private readonly WebView _web;
    private readonly Label _status;
    private bool _saved;

    public SignInPage(DownloadManager manager, SignInSite site, Action? onSignedIn = null)
    {
        _manager = manager;
        _site = site;
        _onSignedIn = onSignedIn;
        Title = $"Sign in to {site.Name}";
        _web = new WebView { Source = site.LoginUrl };
        _status = new Label
        {
            Text = $"Sign in with your {site.Name} account. Your password goes only to {site.Name}.",
            LineBreakMode = LineBreakMode.WordWrap, Margin = new Thickness(12, 8),
        };
        var grid = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)] };
        grid.Add(_status);
        grid.Add(_web, 0, 1);
        Content = grid;
        _web.Navigated += async (_, e) => await CheckAsync(e.Url);
    }

    private async Task CheckAsync(string? url)
    {
        if (_saved || url is null || !url.Contains(_site.Domain, StringComparison.OrdinalIgnoreCase)) return;
        var cookies = await ReadCookiesAsync();
        if (!cookies.Any(c => c.Name == _site.LoggedInCookie))
        {
            _status.Text = $"Not signed in to {_site.Name} yet. Finish signing in.";
            return;
        }
        SaveMerged(_manager, _site, cookies);
        _saved = true;
        _status.Text = "Signed in. You can go back now.";
        _onSignedIn?.Invoke();
        await Navigation.PopAsync();
    }

    /// <summary>Replaces this site's cookies in the shared sign-in file, keeping other sites' sign-ins.</summary>
    private static void SaveMerged(DownloadManager manager, SignInSite site, List<CookieEntry> siteCookies)
    {
        var path = manager.SignInCookiesPath;
        var keep = File.Exists(path)
            ? CookieFile.Read(path).Where(c => !BelongsTo(c.Domain, site)).Select(ToEntry).ToList()
            : new List<CookieEntry>();
        CookieFile.Write(path, keep.Concat(siteCookies));
        manager.Settings.CookiesFile = path;
        manager.Settings.CookiesFromBrowser = null;
        manager.SaveSettings();
    }

    private static bool BelongsTo(string domain, SignInSite site)
    {
        var d = domain.TrimStart('.');
        return site.Origins.Any(o => new Uri(o).Host.EndsWith(d, StringComparison.OrdinalIgnoreCase) || d.EndsWith(new Uri(o).Host.Replace("www.", ""), StringComparison.OrdinalIgnoreCase));
    }

    private static CookieEntry ToEntry(System.Net.Cookie c) =>
        new(c.Domain, c.Path, c.Name, c.Value, c.Secure, c.HttpOnly, c.Expires == DateTime.MinValue ? null : c.Expires.ToUniversalTime());

    private async Task<List<CookieEntry>> ReadCookiesAsync()
    {
        var all = new Dictionary<(string, string), CookieEntry>();
#if WINDOWS
        if (_web.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 { CoreWebView2: { } core })
        {
            foreach (var origin in _site.Origins)
                foreach (var c in await core.CookieManager.GetCookiesAsync(origin))
                    all[(c.Domain, c.Name)] = new CookieEntry(c.Domain, c.Path, c.Name, c.Value, c.IsSecure, c.IsHttpOnly,
                        c.IsSession ? null : DateTimeOffset.FromUnixTimeMilliseconds((long)(c.Expires * 1000)).UtcDateTime);
        }
#elif ANDROID
        await Task.CompletedTask;
        var cm = Android.Webkit.CookieManager.Instance;
        foreach (var origin in _site.Origins)
        {
            var header = cm?.GetCookie(origin);
            if (string.IsNullOrEmpty(header)) continue;
            var domain = "." + new Uri(origin).Host.Replace("www.", "").Replace("accounts.", "");
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

    public static bool IsSignedIn(DownloadManager manager, SignInSite site) =>
        File.Exists(manager.SignInCookiesPath) &&
        CookieFile.Read(manager.SignInCookiesPath).Any(c => c.Name == site.LoggedInCookie && c.Domain.Contains(site.Domain, StringComparison.OrdinalIgnoreCase));

    /// <summary>Removes one site's sign-in (other sites stay signed in).</summary>
    public static void SignOut(DownloadManager manager, SignInSite site)
    {
        var path = manager.SignInCookiesPath;
        if (File.Exists(path))
        {
            var keep = CookieFile.Read(path).Where(c => !BelongsTo(c.Domain, site)).Select(ToEntry).ToList();
            if (keep.Count > 0) CookieFile.Write(path, keep);
            else
            {
                try { File.Delete(path); } catch (IOException) { }
                if (string.Equals(manager.Settings.CookiesFile, path, StringComparison.OrdinalIgnoreCase)) manager.Settings.CookiesFile = null;
            }
        }
        manager.SaveSettings();
#if ANDROID
        Android.Webkit.CookieManager.Instance?.RemoveAllCookies(null);
#endif
    }
}
