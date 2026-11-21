using Bdm.App.Services;
using Bdm.App.ViewModels;
using Bdm.App.Views;
using Bdm.Core;
using CommunityToolkit.Maui;

namespace Bdm.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var platform = new PlatformServices();
        AppLog.Initialize(platform.DataDirectory);
        AppLog.Info($"Starting {Branding.Product} {AppInfo.Current.VersionString} on {DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { AppLog.Error("Unobserved task exception", e.Exception); e.SetObserved(); };

        var manager = new DownloadManager(platform.DataDirectory, platform.DefaultDownloadFolder);
#if ANDROID
        manager.PlatformMuxer = new Droid.AndroidStreamMuxer(); // YouTube HD on phones: merge video + audio with MediaMuxer
#endif
        AppLog.Info($"Loaded {manager.Jobs.Count} download(s) from {platform.DataDirectory}");

        builder.Services.AddSingleton<IPlatformServices>(platform);
        builder.Services.AddSingleton(manager);
        builder.Services.AddSingleton<DownloadsViewModel>();
        builder.Services.AddSingleton<DownloadsPage>();
        builder.Services.AddSingleton<GrabberPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<AboutPage>();
        return builder.Build();
    }
}
