using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using VeloxDM.App.Services;
using VeloxDM.App.ViewModels;
using VeloxDM.Core;

namespace VeloxDM.App.Views;

public partial class MainWindow : Window
{
    private static readonly string[] FixedCategories = ["All", "Active", "Unfinished", "Completed"];

    private readonly DownloadManager _manager;
    private readonly ObservableCollection<DownloadItemViewModel> _items = new();
    private readonly ICollectionView _view;
    private readonly DispatcherTimer _timer;
    private DateTime _lastTick = DateTime.UtcNow;
    private ClipboardMonitor? _clipboard;
    private TrayIcon? _tray;
    private AddDownloadWindow? _openAddWindow;
    private bool _exiting;
    private bool _trayHintShown;
    private string _category = "All";

    public MainWindow(DownloadManager manager)
    {
        InitializeComponent();
        _manager = manager;

        foreach (var job in manager.Jobs.OrderByDescending(j => j.AddedUtc)) _items.Add(new DownloadItemViewModel(job));
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = o => ((DownloadItemViewModel)o).MatchesCategory(_category);
        Grid.ItemsSource = _view;

        CategoryList.ItemsSource = FixedCategories.Concat(UrlClassifier.AllCategories).ToList();
        CategoryList.SelectedIndex = 0;

        manager.JobAdded += job => Dispatcher.BeginInvoke(() => _items.Insert(0, new DownloadItemViewModel(job)));
        manager.JobRemoved += job => Dispatcher.BeginInvoke(() =>
        {
            var vm = _items.FirstOrDefault(i => i.Job == job);
            if (vm is not null) _items.Remove(vm);
        });
        manager.JobFinished += job => Dispatcher.BeginInvoke(() => OnJobFinished(job));

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher);
        _timer.Start();

        Drop += OnDrop;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        InputBindings.Add(new KeyBinding(new RelayCommand(() => ShowAddDialog(null)), Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(PasteFromClipboard), Key.V, ModifierKeys.Control));

        SourceInitialized += (_, _) =>
        {
            _clipboard = new ClipboardMonitor(this) { Enabled = _manager.Settings.MonitorClipboard };
            _clipboard.UrlCaptured += url => ShowAddDialog(url, fromClipboard: true);
            UpdateClipboardStatus();
        };
        _tray = new TrayIcon(ShowFromTray, PasteFromClipboard, _manager.PauseAll, ExitApp);
        _tray.BalloonClicked += ShowFromTray;
        Tick();
    }

    // ------------------------------------------------------------------ external input

    /// <summary>Called for command-line arguments and messages from a second instance.</summary>
    public void HandleExternalRequest(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) { ShowFromTray(); return; }
        ShowAddDialog(message);
    }

    private void PasteFromClipboard()
    {
        string? text = null;
        try { text = Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch (System.Runtime.InteropServices.COMException) { }
        ShowAddDialog(UrlClassifier.ExtractUrl(text));
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.Text) is string text && UrlClassifier.ExtractUrl(text) is { } url)
            ShowAddDialog(url);
    }

    public void ShowAddDialog(string? url, bool fromClipboard = false)
    {
        if (_openAddWindow is not null)
        {
            // A dialog is already open; only update it when the user explicitly asked.
            if (!fromClipboard && url is not null) _openAddWindow.SetUrl(url);
            _openAddWindow.Activate();
            return;
        }

        var dlg = new AddDownloadWindow(_manager, url);
        bool visible = IsVisible && WindowState != WindowState.Minimized;
        if (visible) dlg.Owner = this;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (fromClipboard) dlg.Topmost = true; // copying in a browser should pop the dialog on top

        _openAddWindow = dlg;
        if (_clipboard is not null) _clipboard.Suppress = true;
        try
        {
            if (dlg.ShowDialog() == true && dlg.Result is { } job)
                _manager.Add(job, dlg.StartNow);
        }
        finally
        {
            _openAddWindow = null;
            if (_clipboard is not null) _clipboard.Suppress = false;
        }
    }

    // ------------------------------------------------------------------ refresh

    private void Tick()
    {
        var now = DateTime.UtcNow;
        var elapsed = now - _lastTick;
        _lastTick = now;

        double totalSpeed = 0;
        int active = 0;
        foreach (var item in _items)
        {
            item.Refresh(elapsed);
            totalSpeed += item.Speed;
            if (item.Job.IsRunning) active++;
        }

        if (_category is "Active" or "Unfinished" or "Completed") RefreshFilterKeepingSelection();

        EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = active > 0
            ? $"{active} downloading  ·  {Format.Speed(totalSpeed)}  ·  {_items.Count} in list"
            : $"{_items.Count} in list";
        Title = active > 0 ? $"VeloxDM — {Format.Speed(totalSpeed)}" : "VeloxDM";
        _tray?.SetTooltip(active > 0 ? $"VeloxDM: {active} active, {Format.Speed(totalSpeed)}" : "VeloxDM");
        UpdateDetails();
    }

    private int _filterCountdown;

    private void RefreshFilterKeepingSelection()
    {
        // Status-based filters change as downloads progress; refresh every few ticks.
        if (++_filterCountdown < 4) return;
        _filterCountdown = 0;
        var selected = Grid.SelectedItems.Cast<object>().ToList();
        _view.Refresh();
        foreach (var s in selected) if (_view.Contains(s)) Grid.SelectedItems.Add(s);
    }

    private void UpdateDetails()
    {
        if (Grid.SelectedItem is not DownloadItemViewModel vm)
        {
            Details.Visibility = Visibility.Collapsed;
            return;
        }
        var job = vm.Job;
        Details.Visibility = Visibility.Visible;
        DetailName.Text = vm.FileName;
        DetailUrl.Text = job.Url;
        DetailPath.Text = Path.Combine(job.Folder, job.FileName);

        if (job.Kind == DownloadKind.Http)
        {
            Segments.Visibility = Visibility.Visible;
            Segments.Update(job.SnapshotSegments(), job.TotalBytes ?? 0);
        }
        else
        {
            Segments.Visibility = Visibility.Collapsed;
        }

        var parts = new List<string>();
        if (job.TotalBytes is > 0) parts.Add($"{Format.Bytes(job.DownloadedBytes)} of {Format.Bytes(job.TotalBytes.Value)}");
        if (job.Kind == DownloadKind.Http)
        {
            parts.Add(job.Segments.Count > 0 && !job.SupportsRanges ? "Server does not support resume (single connection)" : $"{job.Segments.Count} segments, {vm.Connections} active connections");
        }
        else
        {
            parts.Add("Video / stream via yt-dlp");
        }
        if (job.Error is { } err) parts.Add("Error: " + err);
        DetailInfo.Text = string.Join("   ·   ", parts);
        SignInButton.Visibility = job.NeedsSignIn && job.Status == DownloadStatus.Failed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (new YouTubeSignInWindow(_manager) { Owner = this }.ShowDialog() != true) return;
        // Retry everything that failed only because a sign-in was missing.
        foreach (var job in _manager.Jobs.Where(j => j.NeedsSignIn && j.Status == DownloadStatus.Failed))
            _manager.Resume(job);
    }

    private void UpdateClipboardStatus() =>
        ClipboardStatus.Text = _manager.Settings.MonitorClipboard ? "Clipboard capture: on" : "Clipboard capture: off";

    private void OnJobFinished(DownloadJob job)
    {
        if (!_manager.Settings.ShowCompletionNotification || _tray is null) return;
        if (job.Status == DownloadStatus.Completed) _tray.Notify("Download complete", job.FileName);
        else if (job.Status == DownloadStatus.Failed) _tray.Notify("Download failed", $"{job.FileName}\n{job.Error}");
    }

    // ------------------------------------------------------------------ toolbar

    private IEnumerable<DownloadItemViewModel> Selected => Grid.SelectedItems.Cast<DownloadItemViewModel>().ToList();

    private void Add_Click(object sender, RoutedEventArgs e) => ShowAddDialog(null);

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        foreach (var vm in Selected) _manager.Resume(vm.Job);
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        foreach (var vm in Selected) _manager.Pause(vm.Job);
    }

    private void Redownload_Click(object sender, RoutedEventArgs e)
    {
        foreach (var vm in Selected) _manager.Redownload(vm.Job);
    }

    private void ResumeAll_Click(object sender, RoutedEventArgs e) => _manager.ResumeAll();
    private void PauseAll_Click(object sender, RoutedEventArgs e) => _manager.PauseAll();

    private void Delete_Click(object sender, RoutedEventArgs e) => RemoveSelected(deleteFiles: false);
    private void DeleteWithFile_Click(object sender, RoutedEventArgs e) => RemoveSelected(deleteFiles: true);

    private async void RemoveSelected(bool deleteFiles)
    {
        var items = Selected.ToList();
        if (items.Count == 0) return;
        var question = deleteFiles
            ? $"Remove {items.Count} download(s) and delete the downloaded file(s) from disk?"
            : $"Remove {items.Count} download(s) from the list? Downloaded files are kept; unfinished partial data is discarded.";
        if (MessageBox.Show(this, question, "VeloxDM", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var vm in items) await _manager.RemoveAsync(vm.Job, deleteFiles);
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is DownloadItemViewModel vm) OpenFile(vm);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not DownloadItemViewModel vm) return;
        var path = vm.Job.FullPath;
        if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Directory.Exists(vm.Job.Folder)) Process.Start("explorer.exe", $"\"{vm.Job.Folder}\"");
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not DownloadItemViewModel vm) return;
        if (_clipboard is not null) _clipboard.Suppress = true;
        try { Clipboard.SetText(vm.Job.Url); } catch (System.Runtime.InteropServices.COMException) { }
        Dispatcher.BeginInvoke(() => { if (_clipboard is not null) _clipboard.Suppress = false; }, DispatcherPriority.ApplicationIdle);
    }

    private void Grabber_Click(object sender, RoutedEventArgs e) => GrabberWindow.Open(_manager, null);

    private void Tools_Click(object sender, RoutedEventArgs e) => new ToolsWindow(_manager) { Owner = this }.ShowDialog();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (new SettingsWindow(_manager) { Owner = this }.ShowDialog() == true)
        {
            if (_clipboard is not null) _clipboard.Enabled = _manager.Settings.MonitorClipboard;
            UpdateClipboardStatus();
            _manager.Pump();
        }
    }

    private void Grid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not DownloadItemViewModel vm) return;
        if (vm.Job.Status == DownloadStatus.Completed) OpenFile(vm);
        else if (vm.Job.IsRunning) _manager.Pause(vm.Job);
        else _manager.Resume(vm.Job);
    }

    private void Grid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { RemoveSelected(deleteFiles: false); e.Handled = true; }
        else if (e.Key == Key.Enter) { OpenFile_Click(sender, e); e.Handled = true; }
    }

    private void Grid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateDetails();

    private void Category_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _category = CategoryList.SelectedItem as string ?? "All";
        _view?.Refresh();
    }

    private void OpenFile(DownloadItemViewModel vm)
    {
        var path = vm.Job.FullPath;
        if (vm.Job.Status != DownloadStatus.Completed || !File.Exists(path))
        {
            MessageBox.Show(this, "The file is not available (not finished, moved or deleted).", "VeloxDM");
            return;
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    // ------------------------------------------------------------------ window / tray lifecycle

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && _manager.Settings.MinimizeToTray) Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && _manager.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray?.Notify("VeloxDM is still running", "It keeps downloading and watching the clipboard. Right-click the tray icon to exit.");
            }
            return;
        }
        base.OnClosing(e);
        if (!_exiting) ExitApp();
    }

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _timer.Stop();
        _clipboard?.Dispose();
        _tray?.Dispose();
        _manager.Dispose();
        Close();
        Application.Current.Shutdown();
    }
}

internal sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
