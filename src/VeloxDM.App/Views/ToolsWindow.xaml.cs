using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VeloxDM.Core;

namespace VeloxDM.App.Views;

public partial class ToolsWindow : Window
{
    private readonly DownloadManager _manager;
    private CancellationTokenSource? _cts;

    public ToolsWindow(DownloadManager manager)
    {
        InitializeComponent();
        _manager = manager;
        Refresh();
    }

    private void Refresh()
    {
        var t = _manager.Tools;
        Show(YtDlpStatus, YtDlpButton, t.YtDlpPath, "Update");
        Show(FfmpegStatus, FfmpegButton, t.FfmpegPath, "Reinstall");
        Show(DenoStatus, DenoButton, t.DenoPath, "Reinstall");
        if (t.DenoPath is null && t.NodePath is { } node)
            DenoStatus.Text = "Not installed. Your Node.js will be used instead: " + node;
    }

    private static void Show(TextBlock status, Button button, string? path, string installedVerb)
    {
        status.Text = path is null ? "Not installed" : "Installed: " + path;
        button.Content = path is null ? "Install" : installedVerb;
    }

    private async Task Run(Func<HttpClient, IProgress<string>, CancellationToken, Task> action)
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        YtDlpButton.IsEnabled = FfmpegButton.IsEnabled = DenoButton.IsEnabled = false;
        var progress = new Progress<string>(s => Progress.Text = s);
        try
        {
            await action(_manager.Http, progress, _cts.Token);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { Progress.Text = "Cancelled."; }
        catch (OperationCanceledException) { Progress.Text = "Failed: the connection timed out. Check your internet connection and try again."; }
        catch (Exception ex) { Progress.Text = "Failed: " + ex.Message; }
        finally
        {
            _cts.Dispose();
            _cts = null;
            YtDlpButton.IsEnabled = FfmpegButton.IsEnabled = DenoButton.IsEnabled = true;
            Refresh();
        }
    }

    private async void YtDlp_Click(object sender, RoutedEventArgs e) => await Run(_manager.Tools.YtDlpPath is null
        ? _manager.Tools.InstallYtDlpAsync
        : async (_, progress, ct) => { progress.Report("Checking for yt-dlp updates..."); progress.Report(await _manager.Tools.UpdateYtDlpAsync(ct)); });
    private async void Ffmpeg_Click(object sender, RoutedEventArgs e) => await Run(_manager.Tools.InstallFfmpegAsync);
    private async void Deno_Click(object sender, RoutedEventArgs e) => await Run(_manager.Tools.InstallDenoAsync);

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_manager.Tools.ToolsDir);
        Process.Start("explorer.exe", $"\"{_manager.Tools.ToolsDir}\"");
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        base.OnClosed(e);
    }
}
