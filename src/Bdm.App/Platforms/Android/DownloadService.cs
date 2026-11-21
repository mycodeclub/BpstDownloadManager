using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;
using AndroidX.Core.App;
using Bdm.App.Services;
using Bdm.Core;
using DownloadManager = Bdm.Core.DownloadManager;
using DownloadStatus = Bdm.Core.DownloadStatus;

namespace Bdm.App.Droid;

/// <summary>
/// Foreground service that keeps downloads running while BDM is in the background or the screen is off,
/// with a progress notification. Holds a partial wake lock and a Wi-Fi lock while active.
/// Note: Android 15 limits "data sync" foreground services to about 6 hours per day.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class DownloadService : Service
{
    private const string ChannelId = "downloads";
    private const string DoneChannelId = "finished";
    private const int NotificationId = 1001;

    private PowerManager.WakeLock? _wakeLock;
    private WifiManager.WifiLock? _wifiLock;
    private Timer? _timer;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        EnsureChannels(this);
        var notification = BuildNotification("Starting downloads…", 0, 0);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
        else
            StartForeground(NotificationId, notification);

        _wakeLock ??= ((PowerManager)GetSystemService(PowerService)!).NewWakeLock(WakeLockFlags.Partial, "BDM:downloads");
        if (!_wakeLock.IsHeld) _wakeLock.Acquire();
#pragma warning disable CA1422 // WifiLock full-high-perf is deprecated on new Android but still helps older devices
        _wifiLock ??= ((WifiManager)GetSystemService(WifiService)!).CreateWifiLock(Android.Net.WifiMode.FullHighPerf, "BDM:downloads");
#pragma warning restore CA1422
        if (!_wifiLock!.IsHeld) _wifiLock.Acquire();

        _timer ??= new Timer(_ => Update(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        return StartCommandResult.Sticky;
    }

    private long _lastBytes;
    private DateTime _lastAt = DateTime.UtcNow;

    private void Update()
    {
        try
        {
            var manager = AppNavigation.Get<DownloadManager>();
            var running = manager.Jobs.Where(j => j.IsRunning).ToList();
            if (running.Count == 0) { StopSelf(); return; }

            long done = running.Sum(j => j.DownloadedBytes);
            long total = running.Sum(j => j.TotalBytes ?? 0);
            var now = DateTime.UtcNow;
            double speed = Math.Max(0, done - _lastBytes) / Math.Max(0.5, (now - _lastAt).TotalSeconds);
            _lastBytes = done;
            _lastAt = now;

            var title = running.Count == 1 ? running[0].FileName : $"{running.Count} downloads";
            var text = $"{Format.Bytes(done)}{(total > 0 ? " / " + Format.Bytes(total) : "")} · {Format.Speed(speed)}";
            int pct = total > 0 ? (int)(done * 100 / total) : 0;
            NotificationManagerCompat.From(this).Notify(NotificationId, BuildNotification(title, pct, total > 0 ? 100 : 0, text));
        }
        catch (Exception ex)
        {
            AppLog.Error("Download notification update failed", ex);
        }
    }

    private Notification BuildNotification(string title, int progress, int max, string? text = null)
    {
        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        return new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle(title)!
            .SetContentText(text ?? "")!
            .SetSmallIcon(Android.Resource.Drawable.StatSysDownload)!
            .SetOngoing(true)!
            .SetOnlyAlertOnce(true)!
            .SetProgress(max, progress, max == 0)!
            .SetContentIntent(open)!
            .Build()!;
    }

    public static void NotifyFinished(Context context, DownloadJob job)
    {
        EnsureChannels(context);
        bool ok = job.Status == DownloadStatus.Completed;
        var n = new NotificationCompat.Builder(context, DoneChannelId)
            .SetContentTitle(ok ? "Download complete" : "Download failed")!
            .SetContentText(ok ? job.FileName : $"{job.FileName}: {job.Error}")!
            .SetSmallIcon(ok ? Android.Resource.Drawable.StatSysDownloadDone : Android.Resource.Drawable.StatNotifyError)!
            .SetAutoCancel(true)!
            .Build()!;
        try { NotificationManagerCompat.From(context).Notify(job.Id.GetHashCode(), n); }
        catch (Java.Lang.SecurityException) { /* notifications not allowed */ }
    }

    private static void EnsureChannels(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        var nm = (NotificationManager)context.GetSystemService(NotificationService)!;
        nm.CreateNotificationChannel(new NotificationChannel(ChannelId, "Active downloads", NotificationImportance.Low));
        nm.CreateNotificationChannel(new NotificationChannel(DoneChannelId, "Finished downloads", NotificationImportance.Default));
    }

    public override void OnDestroy()
    {
        _timer?.Dispose();
        _timer = null;
        if (_wakeLock?.IsHeld == true) _wakeLock.Release();
        if (_wifiLock?.IsHeld == true) _wifiLock.Release();
        base.OnDestroy();
    }
}
