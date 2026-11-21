using Bdm.App.Services;

namespace Bdm.App.WinUI;

/// <summary>WinUI entry point.</summary>
public partial class App : MauiWinUIApplication
{
    public App()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (!SingleInstance.Claim(args))
        {
            Environment.Exit(0); // the running BDM got the link
            return;
        }

        if (args.Length > 0)
        {
            var arg = string.Join(" ", args);
            Bdm.App.App.PendingIncoming = arg.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && File.Exists(arg)
                ? (null, arg)
                : (arg, null);
        }

        SingleInstance.Listen(message => MainThread.BeginInvokeOnMainThread(async () =>
        {
            AppNavigation.Get<IPlatformServices>().BringToFront();
            if (message.Length == 0) return;
            if (message.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && File.Exists(message))
                await AppNavigation.HandleIncomingAsync(null, message);
            else
                await AppNavigation.HandleIncomingAsync(message);
        }));

        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
