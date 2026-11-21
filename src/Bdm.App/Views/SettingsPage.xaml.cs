using System.Globalization;
using Bdm.App.Services;
using Bdm.Core;
using Bdm.Core.Media;

namespace Bdm.App.Views;

public partial class SettingsPage : ContentPage
{
    private readonly DownloadManager _manager;
    private readonly IPlatformServices _platform;
    private CancellationTokenSource? _toolsCts;

    public SettingsPage(DownloadManager manager, IPlatformServices platform)
    {
        InitializeComponent();
        _manager = manager;
        _platform = platform;
        QualityPicker.ItemsSource = MediaQuality.Options.ToList();
        FolderButton.IsVisible = platform.CanChooseFolder;
        FolderEntry.IsReadOnly = !platform.CanChooseFolder;
        ToolsCard.IsVisible = platform.SupportsVideoTools;
        ClipboardRow.IsVisible = DeviceInfo.Idiom == DeviceIdiom.Desktop;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Load();
    }

    private void Load()
    {
        var s = _manager.Settings;
        FolderEntry.Text = s.DownloadFolder;
        ConnSlider.Value = Math.Clamp(s.Connections, 1, 32);
        ConcSlider.Value = Math.Clamp(s.MaxConcurrentDownloads, 1, 10);
        GlobalLimitEntry.Text = s.GlobalSpeedLimitKBps.ToString(CultureInfo.InvariantCulture);
        FocusEntry.Text = s.FocusOthersKBps.ToString(CultureInfo.InvariantCulture);
        RetryEntry.Text = s.AutoRetryCount.ToString(CultureInfo.InvariantCulture);
        KeepAwakeSwitch.IsToggled = s.KeepAwake;
        ClipboardSwitch.IsToggled = s.MonitorClipboard;
        NotifySwitch.IsToggled = s.ShowCompletionNotification;
        PortEntry.Text = s.TorrentPort.ToString(CultureInfo.InvariantCulture);
        RatioEntry.Text = s.TorrentSeedRatio.ToString("0.##", CultureInfo.InvariantCulture);
        UploadEntry.Text = s.TorrentUploadLimitKBps.ToString(CultureInfo.InvariantCulture);
        DhtSwitch.IsToggled = s.TorrentDht;
        UpnpSwitch.IsToggled = s.TorrentPortForwarding;
        QualityPicker.SelectedItem = MediaQuality.Options.FirstOrDefault(o => o.Id == s.DefaultMediaQuality) ?? MediaQuality.Options[0];
        UpdateLabels();
        UpdateSignIn();
        UpdateTools();
        SavedLabel.Text = "";
    }

    private void Slider_Changed(object? sender, ValueChangedEventArgs e) => UpdateLabels();

    private void UpdateLabels()
    {
        ConnLabel.Text = $"Connections per download: {(int)Math.Round(ConnSlider.Value)}";
        ConcLabel.Text = $"Downloads at the same time: {(int)Math.Round(ConcSlider.Value)}";
    }

    private void UpdateSignIn()
    {
        bool signedIn = _manager.IsYouTubeSignedIn;
        SignInStatus.Text = signedIn ? "Signed in to YouTube in BDM." : "Not signed in.";
        SignOutButton.IsEnabled = signedIn;
    }

    private void UpdateTools()
    {
        if (!_platform.SupportsVideoTools) return;
        var t = _manager.Tools;
        YtDlpStatus.Text = t.YtDlpPath is null ? "yt-dlp: not installed" : "yt-dlp: " + t.YtDlpPath;
        YtDlpButton.Text = t.YtDlpPath is null ? "Install" : "Update";
        FfmpegStatus.Text = t.FfmpegPath is null ? "FFmpeg: not installed" : "FFmpeg: " + t.FfmpegPath;
        FfmpegButton.Text = t.FfmpegPath is null ? "Install" : "Reinstall";
        DenoStatus.Text = t.DenoPath is not null ? "Deno: " + t.DenoPath : t.NodePath is not null ? "Deno: not needed, using Node.js" : "Deno: not installed";
        DenoButton.Text = t.DenoPath is null ? "Install" : "Reinstall";
    }

    private static int ParseInt(string? s, int fallback, int min, int max) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, min, max) : fallback;

    private async void Save_Clicked(object? sender, EventArgs e)
    {
        var s = _manager.Settings;
        var folder = FolderEntry.Text?.Trim();
        if (!string.IsNullOrEmpty(folder))
        {
            try { Directory.CreateDirectory(folder); s.DownloadFolder = folder; }
            catch (Exception ex) { await DisplayAlertAsync("Can't use this folder", ex.Message, "OK"); return; }
        }
        s.Connections = (int)Math.Round(ConnSlider.Value);
        s.MaxConcurrentDownloads = (int)Math.Round(ConcSlider.Value);
        s.GlobalSpeedLimitKBps = ParseInt(GlobalLimitEntry.Text, s.GlobalSpeedLimitKBps, 0, 10_000_000);
        s.FocusOthersKBps = ParseInt(FocusEntry.Text, s.FocusOthersKBps, 0, 10_000_000);
        s.AutoRetryCount = ParseInt(RetryEntry.Text, s.AutoRetryCount, 0, 1000);
        s.KeepAwake = KeepAwakeSwitch.IsToggled;
        s.MonitorClipboard = ClipboardSwitch.IsToggled;
        s.ShowCompletionNotification = NotifySwitch.IsToggled;
        s.TorrentPort = ParseInt(PortEntry.Text, s.TorrentPort, 1024, 65535);
        s.TorrentSeedRatio = double.TryParse(RatioEntry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? Math.Clamp(r, 0, 100) : s.TorrentSeedRatio;
        s.TorrentUploadLimitKBps = ParseInt(UploadEntry.Text, s.TorrentUploadLimitKBps, 0, 10_000_000);
        s.TorrentDht = DhtSwitch.IsToggled;
        s.TorrentPortForwarding = UpnpSwitch.IsToggled;
        s.DefaultMediaQuality = (QualityPicker.SelectedItem as MediaQualityOption)?.Id ?? MediaQuality.Best;
        _manager.SaveSettings();
        _manager.Pump();
        SavedLabel.Text = "Saved.";
    }

    private async void Folder_Clicked(object? sender, EventArgs e)
    {
        var folder = await _platform.PickFolderAsync(FolderEntry.Text);
        if (folder is not null) FolderEntry.Text = folder;
    }

    private async void SignIn_Clicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new SignInPage(_manager, () => MainThread.BeginInvokeOnMainThread(UpdateSignIn)));

    private void SignOut_Clicked(object? sender, EventArgs e)
    {
        SignInPage.SignOut(_manager);
        UpdateSignIn();
    }

    private async Task RunToolAsync(Func<HttpClient, IProgress<string>, CancellationToken, Task> action)
    {
        if (_toolsCts is not null) return;
        _toolsCts = new CancellationTokenSource();
        YtDlpButton.IsEnabled = FfmpegButton.IsEnabled = DenoButton.IsEnabled = false;
        var progress = new Progress<string>(s => ToolsProgress.Text = s);
        try
        {
            await action(_manager.Http, progress, _toolsCts.Token);
        }
        catch (OperationCanceledException) when (_toolsCts.IsCancellationRequested) { ToolsProgress.Text = "Cancelled."; }
        catch (OperationCanceledException) { ToolsProgress.Text = "Failed: the connection timed out."; }
        catch (Exception ex) { ToolsProgress.Text = "Failed: " + ex.Message; }
        finally
        {
            _toolsCts.Dispose();
            _toolsCts = null;
            YtDlpButton.IsEnabled = FfmpegButton.IsEnabled = DenoButton.IsEnabled = true;
            UpdateTools();
        }
    }

    private async void YtDlp_Clicked(object? sender, EventArgs e) => await RunToolAsync(_manager.Tools.YtDlpPath is null
        ? _manager.Tools.InstallYtDlpAsync
        : async (_, p, ct) => { p.Report("Checking for yt-dlp updates…"); p.Report(await _manager.Tools.UpdateYtDlpAsync(ct)); });

    private async void Ffmpeg_Clicked(object? sender, EventArgs e) => await RunToolAsync(_manager.Tools.InstallFfmpegAsync);
    private async void Deno_Clicked(object? sender, EventArgs e) => await RunToolAsync(_manager.Tools.InstallDenoAsync);

    protected override void OnDisappearing()
    {
        _toolsCts?.Cancel();
        base.OnDisappearing();
    }
}
