using System;
using System.Linq;
using System.Threading;
using System.Windows;
using BpstVideoDownloader.App.Services;
using BpstVideoDownloader.Core;

namespace BpstVideoDownloader.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private SingleInstanceServer? _server;

    public static DownloadManager Manager { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.Info($"Starting {Branding.Product} {Branding.Version} args=[{string.Join(" ", e.Args)}] debugger={System.Diagnostics.Debugger.IsAttached}");
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => AppLog.Error("Unhandled exception", ex.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ex) => { AppLog.Error("Unobserved task exception", ex.Exception); ex.SetObserved(); };

        try
        {
            StartCore(e);
        }
        catch (Exception ex)
        {
            AppLog.Error("Startup failed", ex);
            MessageBox.Show($"{Branding.Product} could not start:\n\n{ex.Message}\n\nDetails were written to:\n{AppLog.FilePath}",
                Branding.Product, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void StartCore(StartupEventArgs e)
    {
        if (e.Args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            Environment.Exit(RunSelfTest() ? 0 : 1); // closing the main window already requested shutdown
            return;
        }

        if (e.Args.Contains("--setup-tools", StringComparer.OrdinalIgnoreCase))
        {
            // Run by the installer: fetch video tools, then exit. Does not touch the download list.
            using var setupManager = new DownloadManager(DownloadManager.DefaultDataDir);
            new Views.ToolsSetupWindow(setupManager).ShowDialog();
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, @"Local\BpstVideoDownloader.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            // Forward URLs (or a "show" request) to the running instance.
            AppLog.Info("Another instance is running; handing over to it and exiting.");
            SingleInstanceServer.Send(e.Args.Length > 0 ? e.Args : [""]);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            AppLog.Error("UI exception", ex.Exception);
            MessageBox.Show(ex.Exception.Message, Branding.Product, MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        AppLog.Info("Loading downloads and settings from " + DownloadManager.DefaultDataDir);
        Manager = new DownloadManager(DownloadManager.DefaultDataDir);
        AppLog.Info($"Loaded {Manager.Jobs.Count} download(s). Creating main window.");
        var main = new Views.MainWindow(Manager);
        MainWindow = main;

        _server = new SingleInstanceServer(msg => Dispatcher.BeginInvoke(() => main.HandleExternalRequest(msg)));
        _server.Start();

        bool startHidden = e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        if (!startHidden) main.Show();
        AppLog.Info(startHidden ? "Started hidden in the notification area (--tray)." : "Main window shown.");
        AppLog.Cleanup();

        _ = Manager.AutoUpdateYtDlpAsync();

        foreach (var arg in e.Args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)))
            main.HandleExternalRequest(arg);
    }

    /// <summary>
    /// "--self-test": opens every window against a throwaway data folder and reports success via the exit
    /// code (0 = all good). Catches XAML/markup errors that only show up at runtime. Used by the build check.
    /// </summary>
    private static bool RunSelfTest()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bvd-selftest-" + Guid.NewGuid().ToString("N"));
        bool ok = true;
        var manager = new DownloadManager(dir);
        manager.Settings.MinimizeToTray = false;
        manager.Settings.MonitorClipboard = false;

        void Check(string name, Func<Window> create, bool show = true)
        {
            try
            {
                var w = create();
                if (show)
                {
                    w.Show();
                    w.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    w.Close();
                }
                AppLog.Info($"self-test OK: {name}");
            }
            catch (Exception ex)
            {
                ok = false;
                AppLog.Error($"self-test FAILED: {name}", ex);
            }
        }

        Check("About", () => new Views.AboutWindow());
        Check("Settings", () => new Views.SettingsWindow(manager));
        Check("Video tools", () => new Views.ToolsWindow(manager));
        Check("Add download", () => new Views.AddDownloadWindow(manager, "https://example.com/file.zip"));
        Check("Video grabber", () => Views.GrabberWindow.CreateForSelfTest(manager), show: false);
        Check("YouTube sign-in", () => new Views.YouTubeSignInWindow(manager), show: false);
        Check("Main window", () => new Views.MainWindow(manager)); // closing it disposes the manager

        try { System.IO.Directory.Delete(dir, recursive: true); } catch (Exception) { }
        AppLog.Info(ok ? "self-test PASSED" : "self-test FAILED");
        return ok;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _server?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
