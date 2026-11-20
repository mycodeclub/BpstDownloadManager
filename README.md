# VeloxDM

**A free, open-source download manager for Windows, built with .NET 10 and WPF.**

Copy a link anywhere in Windows and VeloxDM offers to download it. It uses many parallel connections and resumes after pauses, crashes or reboots. Video pages (YouTube and 1000+ other sites) and HLS/DASH streams go through [yt-dlp](https://github.com/yt-dlp/yt-dlp).

> VeloxDM is an independent project. It is not affiliated with, endorsed by or derived from any commercial download manager. All code and artwork are original and MIT licensed.

## Features

- **Multi-connection downloads.** A file is split into byte ranges fetched in parallel (1–32 connections).
- **Dynamic segmentation.** When a connection finishes early, it takes over half of the largest remaining range, so every connection stays busy until the end.
- **Pause and resume.** Per-segment progress is saved, so downloads continue after pausing, closing the app or a crash. Retries use exponential backoff. If a server limits connections (HTTP 429/503), VeloxDM automatically drops to fewer connections.
- **Clipboard capture.** Copy a file link or video link in any app, and the Add dialog pops up.
- **Video and stream downloads** through yt-dlp:
  - Quality presets: Best, MP4, 4K to 360p, MP3 or M4A audio.
  - HLS/DASH fragments download in parallel.
  - FFmpeg merges HD video and audio.
- **One-click tool install.** yt-dlp, FFmpeg and Deno come from their official GitHub releases into `%LOCALAPPDATA%\VeloxDM\tools`.
- **Queue.** Choose how many downloads run at once. Categories: Video, Music, Documents, Compressed, Programs, Images.
- **Live segment map.** Shows each connection's progress for the selected download.
- **Notification-area (tray) mode** with completion notifications.
- **Drag and drop** links onto the window. Ctrl+V adds the link from the clipboard, Ctrl+N opens the Add dialog.
- **Single instance.** `VeloxDM.exe <url>` hands the URL to the running app. This is the hook for browser integration.
- **Windows themes.** Light and dark follow Windows (WPF Fluent theme).

## About `blob:` URLs

A `blob:https://site/…` address is an in-memory object that exists only inside the browser tab that created it. No external program can open it, so a downloader can't fetch it directly. To download such a video, do one of the following:

1. Copy the **page address** (the URL in the address bar). VeloxDM detects video pages and extracts the real stream with yt-dlp.
2. Or open DevTools (F12), go to **Network**, filter for `m3u8` or `mpd`, and copy that stream URL into VeloxDM.

Capturing these streams automatically needs a browser extension (see the roadmap).

## Build

Requirements: Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet build
dotnet test
dotnet run --project src/VeloxDM.App
```

Self-contained single-file build (no .NET install needed on the target PC):

```powershell
dotnet publish src/VeloxDM.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Installer (`dist\VeloxDM-Setup-<version>.exe`, per-user, no admin rights needed). Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`):

```powershell
.\build-installer.ps1 -Version 0.1.0
```

On GitHub, every push builds the installer as a workflow artifact. Pushing a tag such as `v0.1.0` also attaches the installer to a GitHub Release.

> **Smart App Control:** with Smart App Control enforcing, Windows blocks unsigned binaries you build yourself. Locally built `VeloxDM.exe` and the test DLLs won't load. Use the CI build, a code-signed release, or a development machine/VM without Smart App Control.

## Project layout

| Path | Contents |
|---|---|
| `src/VeloxDM.Core` | UI-free engine: `SegmentedDownloader`, `HttpProbe`, `DownloadManager` (queue and persistence), `MediaDownloader` / `MediaTools` (yt-dlp), `UrlClassifier` |
| `src/VeloxDM.App` | WPF app: main window, Add / Settings / Video tools dialogs, clipboard monitor, tray icon, single-instance pipe |
| `tests/VeloxDM.Core.Tests` | xUnit tests against a local Kestrel server (parallel ranges, no-range fallback, pause/resume round-trip) |
| `tools/make-icon.ps1` | Regenerates the app icon |

Data lives in `%LOCALAPPDATA%\VeloxDM`: `downloads.json`, `settings.json` and `tools\`.

## Roadmap

- Browser extension (Chrome/Edge/Firefox) that sends links and detected media streams to VeloxDM through native messaging.
- Speed limiter and scheduler.
- Batch / playlist downloads.
- Per-site credentials and cookies.
- Installer (MSIX/winget) with a signed binary.

## Responsible use

Only download content you have the right to download. Respect copyright and each website's terms of service. yt-dlp, FFmpeg and Deno are separate projects under their own licenses. VeloxDM downloads them on request and runs them as external programs.

## License

[MIT](LICENSE)
