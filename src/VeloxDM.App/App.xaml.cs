using System;
using System.Linq;
using System.Threading;
using System.Windows;
using VeloxDM.App.Services;
using VeloxDM.Core;

namespace VeloxDM.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private SingleInstanceServer? _server;

    public static DownloadManager Manager { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--setup-tools", StringComparer.OrdinalIgnoreCase))
        {
            // Run by the installer: fetch video tools, then exit. Does not touch the download list.
            using var setupManager = new DownloadManager(DownloadManager.DefaultDataDir);
            new Views.ToolsSetupWindow(setupManager).ShowDialog();
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, @"Local\VeloxDM.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            // Forward URLs (or a "show" request) to the running instance.
            SingleInstanceServer.Send(e.Args.Length > 0 ? e.Args : [""]);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.Message, "VeloxDM", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        Manager = new DownloadManager(DownloadManager.DefaultDataDir);
        var main = new Views.MainWindow(Manager);
        MainWindow = main;

        _server = new SingleInstanceServer(msg => Dispatcher.BeginInvoke(() => main.HandleExternalRequest(msg)));
        _server.Start();

        bool startHidden = e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        if (!startHidden) main.Show();

        _ = Manager.AutoUpdateYtDlpAsync();

        foreach (var arg in e.Args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)))
            main.HandleExternalRequest(arg);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _server?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
