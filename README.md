<p align="center"><img src="docs/assets/icon-256.png" width="96" alt=""></p>

<h1 align="center">BpstVideoDownloader</h1>

<p align="center"><strong>A free, open-source download manager for Windows, built with .NET 10 and WPF.</strong><br>
A product of <strong>BitProSoftTech</strong> · <a href="https://bitprosofttech.com">bitprosofttech.com</a> · +(91)-82-9910-1616</p>

<p align="center">
  <a href="https://github.com/mycodeclub/BpstDownloadManager/releases/latest/download/BpstVideoDownloader-Setup.exe"><b>⬇ Download for Windows</b></a> ·
  <a href="https://github.com/mycodeclub/BpstDownloadManager/releases/latest/download/BpstVideoDownloader-portable.exe">Portable .exe</a> ·
  <a href="https://mycodeclub.github.io/BpstDownloadManager/">Project page</a> ·
  <a href="https://github.com/mycodeclub/BpstDownloadManager/releases">All releases</a>
</p>

<p align="center">
  <a href="https://github.com/mycodeclub/BpstDownloadManager/actions/workflows/build.yml"><img src="https://github.com/mycodeclub/BpstDownloadManager/actions/workflows/build.yml/badge.svg" alt="Build status"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT License"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078d4.svg" alt="Windows 10 and 11">
  <img src="https://img.shields.io/badge/.NET-10-512bd4.svg" alt=".NET 10">
</p>

![BpstVideoDownloader main window](docs/assets/screenshot-main.png)

Copy a link anywhere in Windows and BpstVideoDownloader offers to download it. It uses many parallel connections and resumes after pauses, crashes or reboots. Video pages (YouTube and 1000+ other sites) and HLS/DASH streams go through [yt-dlp](https://github.com/yt-dlp/yt-dlp).

> BpstVideoDownloader is an independent project. It is not affiliated with, endorsed by or derived from any commercial download manager. All code and artwork are original and MIT licensed.

## Features

- **Multi-connection downloads.** A file is split into byte ranges fetched in parallel (1–32 connections).
- **Dynamic segmentation.** When a connection finishes early, it takes over half of the largest remaining range, so every connection stays busy until the end.
- **Pause and resume.** Per-segment progress is saved, so downloads continue after pausing, closing the app or a crash. Retries use exponential backoff. If a server limits connections (HTTP 429/503), BpstVideoDownloader automatically drops to fewer connections.
- **Clipboard capture.** Copy a file link or video link in any app, and the Add dialog pops up.
- **Video and stream downloads** through yt-dlp:
  - Quality presets: Best, MP4, 4K to 360p, MP3 or M4A audio.
  - HLS/DASH fragments download in parallel.
  - FFmpeg merges HD video and audio.
- **YouTube sign-in** (built-in WebView2 window) for age-restricted, private and members-only videos. Firefox cookies and `cookies.txt` files also work.
- **Video Grabber** for `blob:` players and sites yt-dlp doesn't support: catches HLS, DASH and MP4 streams while the page plays.
- **One-click tool install.** yt-dlp, FFmpeg and Deno come from their official GitHub releases into `%LOCALAPPDATA%\BpstVideoDownloader\tools`.
- **Queue.** Choose how many downloads run at once. Categories: Video, Music, Documents, Compressed, Programs, Images.
- **Live segment map.** Shows each connection's progress for the selected download.
- **Notification-area (tray) mode** with completion notifications.
- **Drag and drop** links onto the window. Ctrl+V adds the link from the clipboard, Ctrl+N opens the Add dialog.
- **Single instance.** `BpstVideoDownloader.exe <url>` hands the URL to the running app. This is the hook for browser integration.
- **Windows themes.** Light and dark follow Windows (WPF Fluent theme).

## About `blob:` URLs

A `blob:https://site/…` address is an in-memory object that exists only inside the browser tab that created it. No external program can open it, so a downloader can't fetch it directly. Behind the blob, the player loads a normal stream: usually an HLS (`.m3u8`) or DASH (`.mpd`) playlist, or an MP4.

**Video Grabber** (toolbar, or the button that appears when you paste a `blob:` link):

1. Open the video page in BpstVideoDownloader's built-in browser.
2. Press Play.
3. BpstVideoDownloader lists the real streams it sees on the network. Click **Download**.

The page's Referer, Origin, User-Agent and cookies are passed along, so CDNs that check them still work. Pop-up windows are blocked.

DRM-protected (copy-protected) videos, such as those on Netflix, Prime Video or Disney+, can't be downloaded. The grabber tells you when a page uses DRM.

## Install

- **Installer:** [BpstVideoDownloader-Setup.exe](https://github.com/mycodeclub/BpstDownloadManager/releases/latest/download/BpstVideoDownloader-Setup.exe). Per-user, no admin rights, Start menu shortcut and uninstaller. It can download the video tools during setup.
- **Portable:** [BpstVideoDownloader-portable.exe](https://github.com/mycodeclub/BpstDownloadManager/releases/latest/download/BpstVideoDownloader-portable.exe). A single file with .NET built in. Set the `BPSTVD_DATA_DIR` environment variable to keep its data in a folder of your choice.

Release builds aren't code-signed yet, so Windows SmartScreen may show "Windows protected your PC". Click **More info → Run anyway**. Each release lists SHA-256 checksums in `SHA256SUMS.txt`.

## Troubleshooting

- Logs: `%LOCALAPPDATA%\BpstVideoDownloader\logs`. Attach the latest log when you [open an issue](https://github.com/mycodeclub/BpstDownloadManager/issues).
- `BpstVideoDownloader.exe --self-test` opens every window and exits with code 0 if they all work.

## Build

Requirements: Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/mycodeclub/BpstDownloadManager.git
cd BpstDownloadManager
dotnet build
dotnet test
dotnet run --project src/BpstVideoDownloader.App
```

Self-contained single-file build (no .NET install needed on the target PC):

```powershell
dotnet publish src/BpstVideoDownloader.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Installer (`dist\BpstVideoDownloader-Setup-<version>.exe`, per-user, no admin rights needed). Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`):

```powershell
.\build-installer.ps1 -Version 0.1.0
```

On GitHub, every push builds the installer as a workflow artifact. Pushing a tag such as `v0.1.0` also attaches the installer to a GitHub Release.

> **Smart App Control:** with Smart App Control enforcing, Windows blocks unsigned binaries you build yourself. Locally built `BpstVideoDownloader.exe` and the test DLLs won't load. Use the CI build, a code-signed release, or a development machine/VM without Smart App Control.

## Project layout

| Path | Contents |
|---|---|
| `src/BpstVideoDownloader.Core` | UI-free engine: `SegmentedDownloader`, `HttpProbe`, `DownloadManager` (queue and persistence), `MediaDownloader` / `MediaTools` (yt-dlp), `UrlClassifier` |
| `src/BpstVideoDownloader.App` | WPF app: main window, Add / Settings / Video tools dialogs, clipboard monitor, tray icon, single-instance pipe |
| `tests/BpstVideoDownloader.Core.Tests` | xUnit tests against a local Kestrel server (parallel ranges, no-range fallback, pause/resume round-trip) |
| `tools/make-icon.ps1` | Regenerates the app icon |

Data lives in `%LOCALAPPDATA%\BpstVideoDownloader`: `downloads.json`, `settings.json` and `tools\`.

## Roadmap

- Browser extension (Chrome/Edge/Firefox) that sends links and detected media streams to BpstVideoDownloader through native messaging.
- Speed limiter and scheduler.
- Batch / playlist downloads.
- Per-site credentials and cookies.
- Installer (MSIX/winget) with a signed binary.

## Contributing

Issues and pull requests are welcome. Please run `dotnet test` and `--self-test` before opening a pull request. The project website lives in [`docs/`](docs/index.html) and is published with GitHub Pages.

## Responsible use

Only download content you have the right to download. Respect copyright and each website's terms of service. yt-dlp, FFmpeg and Deno are separate projects under their own licenses. BpstVideoDownloader downloads them on request and runs them as external programs.

## License

[MIT](LICENSE)
