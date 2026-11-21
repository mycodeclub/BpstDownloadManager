using System.Text.Json;

namespace Bdm.Core.Tests;

public sealed class MigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bvd-migrate-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Copies_old_data_rewrites_saved_paths_and_removes_old_folder()
    {
        var legacy = Path.Combine(_root, "VeloxDM");
        var dir = Path.Combine(_root, "BpstVideoDownloader");
        Directory.CreateDirectory(Path.Combine(legacy, "tools"));
        File.WriteAllText(Path.Combine(legacy, "tools", "yt-dlp.exe"), "exe");
        File.WriteAllText(Path.Combine(legacy, "youtube-cookies.txt"), "cookies");
        File.WriteAllText(Path.Combine(legacy, "downloads.json"), "[]");
        var settings = new AppSettings { CookiesFile = Path.Combine(legacy, "youtube-cookies.txt") };
        File.WriteAllText(Path.Combine(legacy, "settings.json"), JsonSerializer.Serialize(settings));
        Directory.CreateDirectory(dir); // an empty new folder (e.g. from a first failed attempt) must not block migration

        DownloadManager.MigrateLegacyData(legacy, dir);

        Assert.True(File.Exists(Path.Combine(dir, "tools", "yt-dlp.exe")));
        Assert.True(File.Exists(Path.Combine(dir, "downloads.json")));
        var migrated = AppSettings.Load(Path.Combine(dir, "settings.json"));
        Assert.Equal(Path.Combine(dir, "youtube-cookies.txt"), migrated.CookiesFile);
        Assert.False(Directory.Exists(legacy));
    }
}
