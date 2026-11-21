using Bdm.App.Services;
using Bdm.Core;

namespace Bdm.App;

/// <summary>iOS (prepared, not shipped yet): files go to the app's Documents/Downloads folder (visible in the Files app).</summary>
public sealed class PlatformServices : IPlatformServices
{
    public string DataDirectory => FileSystem.AppDataDirectory;

    public string DefaultDownloadFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Downloads");

    public bool CanChooseFolder => false;
    public bool SupportsVideoTools => false;
    public bool CanShowInFolder => false;

    public Task OpenFileAsync(string path) =>
        Launcher.Default.OpenAsync(new OpenFileRequest(Path.GetFileName(path), new ReadOnlyFile(path)));

    public Task ShowInFolderAsync(string path) => Task.CompletedTask;
    public Task<string?> PickFolderAsync(string? initialFolder) => Task.FromResult<string?>(null);

    // iOS suspends background apps; long downloads will need a background URLSession (future work).
    public void SetBusy(bool busy) => UIKit.UIApplication.SharedApplication.IdleTimerDisabled = busy;

    public Task OnDownloadFinishedAsync(DownloadJob job) => Task.CompletedTask;
    public Task RequestPermissionsAsync() => Task.CompletedTask;
    public void BringToFront() { }
}
