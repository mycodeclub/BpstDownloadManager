using Bdm.Core;

namespace Bdm.App.Services;

/// <summary>Things that work differently on Windows, Android and iOS. Implemented in Platforms/*/PlatformServices.cs.</summary>
public interface IPlatformServices
{
    /// <summary>App data: settings, download list, tools, logs.</summary>
    string DataDirectory { get; }

    /// <summary>Default folder for downloaded files.</summary>
    string DefaultDownloadFolder { get; }

    /// <summary>Whether the user can pick any folder (desktop) or files always go to the standard Downloads folder (mobile).</summary>
    bool CanChooseFolder { get; }

    /// <summary>yt-dlp / FFmpeg / Deno can be installed (desktop only).</summary>
    bool SupportsVideoTools { get; }

    bool CanShowInFolder { get; }

    Task OpenFileAsync(string path);
    Task ShowInFolderAsync(string path);
    Task<string?> PickFolderAsync(string? initialFolder);

    /// <summary>Downloads are running (true) or all idle (false): keep the device awake / run a background service.</summary>
    void SetBusy(bool busy);

    /// <summary>A download finished: make it visible to other apps (media scan on Android), notify.</summary>
    Task OnDownloadFinishedAsync(DownloadJob job);

    /// <summary>Ask for runtime permissions the app needs (notifications, storage on old Android).</summary>
    Task RequestPermissionsAsync();

    /// <summary>Brings the app window to the front (desktop) when a copied link is caught.</summary>
    void BringToFront();
}
