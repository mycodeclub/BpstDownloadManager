using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using BpstVideoDownloader.Core;
using BpstVideoDownloader.Core.Media;

namespace BpstVideoDownloader.App.Views;

/// <summary>
/// Lets the user sign in to YouTube inside BpstVideoDownloader (WebView2, a private profile in %LOCALAPPDATA%\BpstVideoDownloader\webview).
/// The resulting YouTube/Google cookies are saved as a cookies.txt that yt-dlp uses for
/// age-restricted, private and members-only videos. The password is typed into Google's own page;
/// BpstVideoDownloader never sees it.
/// </summary>
public sealed class YouTubeSignInWindow : Window
{
    private const string SignInUrl = "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F";
    private static readonly string[] CookieOrigins = ["https://www.youtube.com", "https://youtube.com", "https://accounts.google.com", "https://www.google.com"];

    private readonly DownloadManager _manager;
    private readonly WebView2 _web = new();
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Button _save = new() { Content = "Save sign-in", MinWidth = 110, IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
    private bool _saved;

    public YouTubeSignInWindow(DownloadManager manager)
    {
        _manager = manager;
        Title = "Sign in to YouTube - BpstVideoDownloader";
        Width = 520;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/velox.ico"));

        var cancel = new Button { Content = "Cancel", MinWidth = 80, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        _save.Click += async (_, _) => await SaveAsync();
        var bar = new DockPanel { Margin = new Thickness(12, 10, 12, 10) };
        DockPanel.SetDock(cancel, Dock.Right);
        DockPanel.SetDock(_save, Dock.Right);
        bar.Children.Add(cancel);
        bar.Children.Add(_save);
        bar.Children.Add(_status);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_web);
        Content = root;

        _status.Text = "Sign in with your Google account. Your password goes only to Google.";
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(_manager.DataDir, "webview"));
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.NavigationCompleted += async (_, _) => await CheckSignedInAsync();
            _web.CoreWebView2.Navigate(SignInUrl);
        }
        catch (Exception ex)
        {
            _status.Text = "Could not start the built-in browser (Microsoft Edge WebView2 Runtime): " + ex.Message;
        }
    }

    private async Task CheckSignedInAsync()
    {
        if (_saved || _web.CoreWebView2 is null) return;
        var cookies = await _web.CoreWebView2.CookieManager.GetCookiesAsync("https://www.youtube.com");
        bool signedIn = cookies.Any(c => c.Name == "LOGIN_INFO") && cookies.Any(c => c.Name.Contains("SAPISID", StringComparison.Ordinal));
        _save.IsEnabled = signedIn;
        if (signedIn && _web.Source?.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) == true)
        {
            _status.Text = "Signed in. Saving...";
            await SaveAsync(); // landed back on YouTube after sign-in: save automatically
        }
    }

    private async Task SaveAsync()
    {
        if (_saved || _web.CoreWebView2 is null) return;
        var all = new Dictionary<(string, string, string), CookieEntry>();
        foreach (var origin in CookieOrigins)
        {
            foreach (var c in await _web.CoreWebView2.CookieManager.GetCookiesAsync(origin))
            {
                var entry = new CookieEntry(c.Domain, c.Path, c.Name, c.Value, c.IsSecure, c.IsHttpOnly,
                    c.IsSession ? null : c.Expires.ToUniversalTime());
                all[(c.Domain, c.Path, c.Name)] = entry;
            }
        }

        if (!all.Values.Any(c => c.Name == "LOGIN_INFO"))
        {
            _status.Text = "Not signed in to YouTube yet. Finish signing in first.";
            return;
        }

        CookieFile.Write(_manager.YouTubeCookiesPath, all.Values);
        _manager.Settings.CookiesFile = _manager.YouTubeCookiesPath;
        _manager.Settings.CookiesFromBrowser = null;
        _manager.SaveSettings();
        _saved = true;
        DialogResult = true;
    }

    /// <summary>Deletes the saved session and the private browser profile.</summary>
    public static void SignOut(DownloadManager manager)
    {
        try { File.Delete(manager.YouTubeCookiesPath); } catch (IOException) { }
        try { Directory.Delete(Path.Combine(manager.DataDir, "webview"), recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        if (string.Equals(manager.Settings.CookiesFile, manager.YouTubeCookiesPath, StringComparison.OrdinalIgnoreCase))
            manager.Settings.CookiesFile = null;
        manager.SaveSettings();
    }

    protected override void OnClosed(EventArgs e)
    {
        _web.Dispose();
        base.OnClosed(e);
    }
}
