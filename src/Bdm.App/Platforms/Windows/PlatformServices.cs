using System.Diagnostics;
using System.Runtime.InteropServices;
using Bdm.App.Services;
using Bdm.Core;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Storage;

namespace Bdm.App;

public sealed class PlatformServices : IPlatformServices
{
    public string DataDirectory => DownloadManager.DefaultDataDir;
    public string DefaultDownloadFolder => DownloadManager.DefaultDownloadFolder();
    public bool CanChooseFolder => true;
    public bool SupportsVideoTools => true;
    public bool CanShowInFolder => true;

    public Task OpenFileAsync(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("The file was moved or deleted.", path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    public Task ShowInFolderAsync(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir)) Process.Start("explorer.exe", $"\"{dir}\"");
        return Task.CompletedTask;
    }

    public async Task<string?> PickFolderAsync(string? initialFolder)
    {
        var result = await FolderPicker.Default.PickAsync(initialFolder ?? DefaultDownloadFolder, CancellationToken.None);
        return result.IsSuccessful ? result.Folder.Path : null;
    }

    public void SetBusy(bool busy) =>
        SetThreadExecutionState(busy ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS);

    public async Task OnDownloadFinishedAsync(DownloadJob job)
    {
        var settings = AppNavigation.Get<DownloadManager>().Settings;
        if (!settings.ShowCompletionNotification) return;
        var text = job.Status == DownloadStatus.Completed ? $"Downloaded: {job.FileName}" : $"Failed: {job.FileName}";
        await MainThread.InvokeOnMainThreadAsync(() => Toast.Make(text).Show());
    }

    public Task RequestPermissionsAsync() => Task.CompletedTask;

    public void BringToFront()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is Microsoft.UI.Xaml.Window w)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(w);
                ShowWindow(hwnd, 9 /* SW_RESTORE */);
                SetForegroundWindow(hwnd);
            }
        });
    }

    private const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
}
