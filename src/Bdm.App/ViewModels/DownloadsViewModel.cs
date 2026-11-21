using System.Collections.ObjectModel;
using Bdm.App.Services;
using Bdm.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bdm.App.ViewModels;

public sealed partial class DownloadsViewModel : ObservableObject
{
    public static readonly string[] Filters = ["All", "Active", "Unfinished", "Completed", "Video", "Torrents", "Programs", "Compressed", "Documents", "Music", "Images", "Other"];

    private readonly DownloadManager _manager;
    private readonly IPlatformServices _platform;
    private readonly List<DownloadItemViewModel> _all = new();
    private DateTime _lastTick = DateTime.UtcNow;
    private int _tickCount;

    public DownloadsViewModel(DownloadManager manager, IPlatformServices platform)
    {
        _manager = manager;
        _platform = platform;
        foreach (var job in manager.Jobs.OrderByDescending(j => j.AddedUtc)) _all.Add(new DownloadItemViewModel(job));
        RebuildVisible();

        manager.JobAdded += job => MainThread.BeginInvokeOnMainThread(() =>
        {
            _all.Insert(0, new DownloadItemViewModel(job));
            RebuildVisible();
        });
        manager.JobRemoved += job => MainThread.BeginInvokeOnMainThread(() =>
        {
            _all.RemoveAll(i => i.Job == job);
            RebuildVisible();
        });
    }

    public ObservableCollection<DownloadItemViewModel> Items { get; } = new();
    public IReadOnlyList<string> FilterNames => Filters;

    [ObservableProperty] private string selectedFilter = "All";
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string summaryText = "";
    [ObservableProperty] private bool isEmpty = true;
    [ObservableProperty] private bool hasFocus;
    [ObservableProperty] private string focusText = "";

    partial void OnSelectedFilterChanged(string value) => RebuildVisible();
    partial void OnSearchTextChanged(string value) => RebuildVisible();

    public string Credit => $"A product of {Branding.Company} · {Branding.Website} · {Branding.Phone}";

    /// <summary>Called every ~700 ms by the page timer.</summary>
    public void Tick()
    {
        var now = DateTime.UtcNow;
        var elapsed = now - _lastTick;
        _lastTick = now;
        var focus = _manager.FocusedJobId;
        double speed = 0;
        int active = 0;
        foreach (var item in _all)
        {
            item.Refresh(elapsed, focus);
            speed += item.Speed;
            if (item.IsRunning) active++;
        }
        if (++_tickCount % 4 == 0 && SelectedFilter is "Active" or "Unfinished" or "Completed") RebuildVisible();

        SummaryText = active > 0 ? $"{active} active · {Format.Speed(speed)} · {_all.Count} in list" : $"{_all.Count} in list";
        var focused = focus is { } f ? _all.FirstOrDefault(i => i.Job.Id == f) : null;
        HasFocus = focused is not null;
        FocusText = focused is null ? "" : $"⚡ Focus: {focused.FileName} gets the full connection. Others are slowed down.";
        IsEmpty = _all.Count == 0;
    }

    private void RebuildVisible()
    {
        var wanted = _all.Where(i => i.Matches(SelectedFilter, SearchText.Trim())).ToList();
        // Minimal changes so the list doesn't flicker or lose scroll position.
        for (int i = Items.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Items[i])) Items.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < Items.Count && Items[i] == wanted[i]) continue;
            int existing = Items.IndexOf(wanted[i]);
            if (existing >= 0) Items.Move(existing, i);
            else Items.Insert(i, wanted[i]);
        }
        IsEmpty = _all.Count == 0;
    }

    [RelayCommand]
    private async Task PrimaryAction(DownloadItemViewModel item)
    {
        var job = item.Job;
        if (job.Status == DownloadStatus.Completed) await OpenAsync(item);
        else if (job.IsRunning) _manager.Pause(job);
        else if (job.Status == DownloadStatus.Failed && job.NeedsSignIn) await Shell.Current.GoToAsync("//settings");
        else _manager.Resume(job);
        item.Refresh(TimeSpan.Zero, _manager.FocusedJobId);
    }

    [RelayCommand]
    private void ToggleFocus(DownloadItemViewModel item)
    {
        _manager.SetFocus(_manager.FocusedJobId == item.Job.Id ? null : item.Job);
        Tick();
    }

    [RelayCommand]
    private void ClearFocus()
    {
        _manager.SetFocus(null);
        Tick();
    }

    [RelayCommand]
    private void StartAll() => _manager.ResumeAll();

    [RelayCommand]
    private void PauseAll() => _manager.PauseAll();

    private async Task OpenAsync(DownloadItemViewModel item)
    {
        try { await _platform.OpenFileAsync(item.Job.FullPath); }
        catch (Exception ex) { await Shell.Current.DisplayAlertAsync("Can't open", ex.Message, "OK"); }
    }

    [RelayCommand]
    private async Task More(DownloadItemViewModel item)
    {
        var job = item.Job;
        var options = new List<string>();
        if (job.Status == DownloadStatus.Completed) options.Add("Open");
        if (_platform.CanShowInFolder) options.Add("Show in folder");
        options.Add("Copy link");
        if (!job.IsRunning) options.Add("Download again");
        options.Add("Remove from list");
        options.Add("Remove and delete file");

        var choice = await Shell.Current.DisplayActionSheetAsync(item.FileName, "Cancel", null, options.ToArray());
        switch (choice)
        {
            case "Open": await OpenAsync(item); break;
            case "Show in folder": await _platform.ShowInFolderAsync(job.FullPath); break;
            case "Copy link": await Clipboard.Default.SetTextAsync(job.Url); break;
            case "Download again": _manager.Redownload(job); break;
            case "Remove from list": await _manager.RemoveAsync(job, deleteFiles: false); break;
            case "Remove and delete file":
                if (await Shell.Current.DisplayAlertAsync("Delete file?", $"Remove \"{item.FileName}\" and delete its downloaded data from this device?", "Delete", "Cancel"))
                    await _manager.RemoveAsync(job, deleteFiles: true);
                break;
        }
    }
}
