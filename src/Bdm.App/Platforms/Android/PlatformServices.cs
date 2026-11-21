using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;
using Bdm.App.Droid;
using Bdm.App.Services;
using Bdm.Core;
using DownloadManager = Bdm.Core.DownloadManager;
using DownloadStatus = Bdm.Core.DownloadStatus;
using AndroidEnvironment = Android.OS.Environment;
using AndroidUri = Android.Net.Uri;

namespace Bdm.App;

public sealed class PlatformServices : IPlatformServices
{
    private static Context Context => Platform.AppContext;

    public string DataDirectory => FileSystem.AppDataDirectory;

    /// <summary>The phone's public Download/BDM folder, visible in the Files app.</summary>
    public string DefaultDownloadFolder =>
        Path.Combine(AndroidEnvironment.GetExternalStoragePublicDirectory(AndroidEnvironment.DirectoryDownloads)!.AbsolutePath, "BDM");

    public bool CanChooseFolder => false;
    public bool SupportsVideoTools => false;
    public bool CanShowInFolder => false;

    public async Task OpenFileAsync(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The file was moved or deleted.", path);
        var uri = await ScanAsync(path) ?? throw new InvalidOperationException("Android couldn't index this file yet. Try again in a moment.");
        var mime = Android.Webkit.MimeTypeMap.Singleton?.GetMimeTypeFromExtension(Path.GetExtension(path).TrimStart('.').ToLowerInvariant()) ?? "*/*";
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, mime);
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
        Context.StartActivity(Intent.CreateChooser(intent, "Open with")!.AddFlags(ActivityFlags.NewTask));
    }

    public Task ShowInFolderAsync(string path) => Task.CompletedTask;
    public Task<string?> PickFolderAsync(string? initialFolder) => Task.FromResult<string?>(null);

    public void SetBusy(bool busy)
    {
        var intent = new Intent(Context, typeof(DownloadService));
        if (busy)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O) Context.StartForegroundService(intent);
            else Context.StartService(intent);
        }
        else
        {
            Context.StopService(intent);
        }
    }

    public async Task OnDownloadFinishedAsync(DownloadJob job)
    {
        if (job.Status == DownloadStatus.Completed)
        {
            // Let the gallery / music / Files apps see the new file(s).
            if (File.Exists(job.FullPath)) await ScanAsync(job.FullPath);
            else if (Directory.Exists(job.FullPath))
                foreach (var f in Directory.EnumerateFiles(job.FullPath, "*", SearchOption.AllDirectories).Take(200)) await ScanAsync(f);
        }
        if (AppNavigation.Get<DownloadManager>().Settings.ShowCompletionNotification)
            DownloadService.NotifyFinished(Context, job);
    }

    private static Task<AndroidUri?> ScanAsync(string path)
    {
        var tcs = new TaskCompletionSource<AndroidUri?>();
        MediaScannerConnection.ScanFile(Context, [path], null, new ScanCallback(uri => tcs.TrySetResult(uri)));
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(10)).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null);
    }

    private sealed class ScanCallback(Action<AndroidUri?> done) : Java.Lang.Object, MediaScannerConnection.IOnScanCompletedListener
    {
        public void OnScanCompleted(string? path, AndroidUri? uri) => done(uri);
    }

    public async Task RequestPermissionsAsync()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            await Permissions.RequestAsync<Permissions.PostNotifications>();
        if (Build.VERSION.SdkInt < BuildVersionCodes.Q)
            await Permissions.RequestAsync<Permissions.StorageWrite>();
    }

    public void BringToFront() { }
}
