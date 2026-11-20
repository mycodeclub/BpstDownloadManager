using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BpstVideoDownloader.Core;

namespace BpstVideoDownloader.App.Views;

/// <summary>
/// Shown when the installer runs "BpstVideoDownloader.exe --setup-tools": downloads the missing video tools
/// (yt-dlp, FFmpeg, and Deno when no Node.js is present) with visible progress, then exits.
/// </summary>
public sealed class ToolsSetupWindow : Window
{
    private readonly DownloadManager _manager;
    private readonly TextBlock _step = new() { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _detail = new() { Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ProgressBar _bar = new() { Height = 6, Margin = new Thickness(0, 14, 0, 0), IsIndeterminate = true };
    private readonly Button _button = new() { Content = "Skip", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
    private readonly CancellationTokenSource _cts = new();
    private bool _done;

    public ToolsSetupWindow(DownloadManager manager)
    {
        _manager = manager;
        Title = "BpstVideoDownloader setup: video tools";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/velox.ico"));
        Content = new StackPanel
        {
            Margin = new Thickness(22),
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14),
                    Text = "Downloading the free open-source tools used for YouTube and other video sites from their official GitHub releases.",
                },
                _step, _detail, _bar, _button,
            },
        };
        _button.Click += (_, _) => { if (_done) Close(); else _cts.Cancel(); };
        Loaded += async (_, _) => await RunAsync();
        Closing += (_, _) => _cts.Cancel();
    }

    private async Task RunAsync()
    {
        var tools = _manager.Tools;
        var progress = new Progress<string>(s => _detail.Text = s);
        var failures = 0;

        async Task Step(string title, bool needed, Func<System.Net.Http.HttpClient, IProgress<string>, CancellationToken, Task> install)
        {
            if (!needed || _cts.IsCancellationRequested) return;
            _step.Text = title;
            _detail.Text = "";
            try { await install(_manager.Http, progress, _cts.Token); }
            catch (Exception) when (!_cts.IsCancellationRequested) { failures++; }
            catch (OperationCanceledException) { }
        }

        await Step("yt-dlp (video site support)", tools.YtDlpPath is null, tools.InstallYtDlpAsync);
        await Step("FFmpeg (merges HD video and audio)", tools.FfmpegPath is null, tools.InstallFfmpegAsync);
        await Step("Deno (needed for YouTube)", tools.JsRuntimePath is null, tools.InstallDenoAsync);

        _done = true;
        _bar.IsIndeterminate = false;
        _bar.Value = _bar.Maximum;
        _button.Content = "Close";
        if (_cts.IsCancellationRequested || failures > 0)
        {
            _step.Text = _cts.IsCancellationRequested ? "Skipped." : "Some tools could not be downloaded.";
            _detail.Text = "You can install them any time from BpstVideoDownloader → Video tools.";
        }
        else
        {
            Close(); // all good: return to the installer right away
        }
    }
}
