using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Bdm.App.Services;

namespace Bdm.App;

/// <summary>
/// Also receives links: "Share → BDM" from Chrome/YouTube (text), magnet: links, and .torrent files.
/// </summary>
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "text/plain")]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "magnet")]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataMimeType = "application/x-bittorrent")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Handle(Intent, coldStart: true);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Handle(intent, coldStart: false);
    }

    private void Handle(Intent? intent, bool coldStart)
    {
        if (intent is null) return;
        try
        {
            string? text = null, torrentPath = null;
            if (intent.Action == Intent.ActionSend)
            {
                text = intent.GetStringExtra(Intent.ExtraText);
            }
            else if (intent.Action == Intent.ActionView && intent.Data is { } data)
            {
                if (data.Scheme == "magnet") text = data.ToString();
                else torrentPath = CopyToCache(data);
            }
            if (text is null && torrentPath is null) return;

            if (coldStart) App.PendingIncoming = (text, torrentPath);
            else _ = AppNavigation.HandleIncomingAsync(text, torrentPath);
        }
        catch (Exception ex)
        {
            AppLog.Error("Couldn't handle incoming link", ex);
        }
    }

    /// <summary>content:// .torrent files are copied to the cache so the torrent engine can read them.</summary>
    private string? CopyToCache(Android.Net.Uri uri)
    {
        using var input = ContentResolver?.OpenInputStream(uri);
        if (input is null) return null;
        var path = Path.Combine(FileSystem.CacheDirectory, Guid.NewGuid().ToString("N") + ".torrent");
        using var output = File.Create(path);
        input.CopyTo(output);
        return path;
    }
}
