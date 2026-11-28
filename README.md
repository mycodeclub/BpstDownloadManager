<p align="center"><img src="docs/assets/icon-256.png" width="96" alt=""></p>

<h1 align="center">BDM: BPST Download Manager</h1>

<p align="center"><strong>Free, open-source download manager for Windows and Android, built with .NET 10 and .NET MAUI.</strong><br>
A product of <strong>BitProSoftTech</strong> · <a href="https://bitprosofttech.com">bitprosofttech.com</a> · +(91)-82-9910-1616</p>

<p align="center">
  <a href="https://github.com/mycodeclub/BpstDownloadManager/releases/latest/download/BDM-Setup.exe"><b>⬇ Windows</b></a> ·
  <a href="https://github.com/mycodeclub/BpstDownloadManager/releases/latest/download/BDM-android.apk"><b>⬇ Android</b></a> ·
  <a href="https://mycodeclub.github.io/BpstDownloadManager/">Project page</a> ·
  <a href="https://github.com/mycodeclub/BpstDownloadManager/releases">All releases</a>
</p>

<p align="center">
  <a href="https://github.com/mycodeclub/BpstDownloadManager/actions/workflows/build.yml"><img src="https://github.com/mycodeclub/BpstDownloadManager/actions/workflows/build.yml/badge.svg" alt="Build status"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT License"></a>
  <img src="https://img.shields.io/badge/platforms-Windows%20%7C%20Android-0078d4.svg" alt="Windows and Android">
  <img src="https://img.shields.io/badge/.NET-10%20LTS-512bd4.svg" alt=".NET 10 LTS">
</p>

![BDM main window](docs/assets/screenshot-main.png)

> BDM is an independent project. It is not affiliated with, endorsed by or derived from any commercial download manager. All code and artwork are original and MIT licensed.

## What it downloads

| | How |
|---|---|
| **Any file**: programs, ZIPs, games, ISOs, multi-GB files | Up to 32 parallel connections, dynamic splitting, resume after pause/crash/reboot, changed-file detection, automatic retry when the network drops |
| **YouTube** | HD/4K video and audio streams downloaded in parallel and merged into one MP4. Windows uses yt-dlp + FFmpeg (installed automatically on first use); Android uses a built-in downloader and the phone's own MediaMuxer |
| **1000+ other video sites** | yt-dlp (Windows) |
| **`blob:` / streaming videos** | Video Grabber: open the page, press Play, download the HLS (`.m3u8`), DASH or MP4 stream it detects. HLS uses a built-in downloader (parallel segments, AES-128, resume) on every platform |
| **Torrents** | Magnet links and `.torrent` files, choose files, DHT, peer exchange, UPnP, fast resume, seeding ratio |
| **Movies and web series** | Smart Finder shows where they stream legally in your country (Netflix, Prime Video, JioHotstar, ZEE5, SonyLIV…), where to rent or buy them, official free streams, and public-domain films BDM can download |

## Highlights

- **⚡ Focus mode.** Give one download the whole connection. The others slow down to a trickle but keep their connections (or pause, your choice), and everything returns to normal when it finishes.
- **Speed limit.** Set an overall limit, so downloads don't take the whole connection while you work.
- **Built for long downloads.** Per-part resume, automatic retries with backoff, a check that the server's file hasn't changed, keep-awake on Windows, and a foreground service with wake/Wi-Fi locks on Android.
- **Catches links.** Clipboard capture on Windows, "Share → BDM" on Android, magnet/.torrent file associations, `BDM.exe <link>`.
- **Facebook and Instagram videos and reels**. Public posts work straight away.
- **Sign-in for YouTube, Instagram and Facebook** (Settings → Video) for age-restricted, private, members-only and login-only videos. You sign in on the site's own page inside BDM, and the session stays on your device.
- **Smart Finder.** Search once and find it legally: games, software, e-books & PDFs, documents, courses, music, **movies and web series**. Category filters (Games, Software, Courses, E-books & PDF, Documents, Music & audio, Movies, Web series & TV, Other) and a Free / Paid filter. Results come in groups:
  - **Watch** (movies and series): the streaming services that have it in your country, such as Netflix, Prime Video, JioHotstar, ZEE5, SonyLIV and Apple TV, with a **language filter** (Hindi, English, Tamil, Telugu, Malayalam, Kannada, Bengali, Marathi, Punjabi, Gujarati, Korean, Japanese…). Type it naturally: *"Die Hard 4 in Hindi"*, *"Panchayat web series"*, *"Mirzapur season 2"*.
  - **Buy / Rent or buy:** official stores with prices (Steam, GOG, Epic, Humble, PlayStation, Xbox, Microsoft Store, Google Play / Google TV, Udemy, Amazon…).
  - **Free:** open-source release files from GitHub (pick the installer and BDM downloads it), free games (Steam free-to-play, free GOG games, Epic's weekly free games, itch.io, Game Jolt, IndieGala freebies), F-Droid and Flathub apps, official free streams with ads, public-domain films, e-books (Project Gutenberg, Internet Archive), open-access papers (arXiv), free music.
  - **Official torrents:** torrents published by the makers themselves (Linux distributions, Internet Archive).

  Cracks, repacks, pirate movie sites and torrent indexes are never shown; for paid games, software and films, BDM points to the official stores and services. Films on streaming services are copy-protected: watch them in the service's app and use its own Download button for offline viewing.

  **Where-to-watch data (optional):** add a free [TMDB](https://www.themoviedb.org/settings/api) API key under **Settings → Smart Finder** and BDM lists exactly which services carry each title in your country (availability data by JustWatch, via TMDB). Without a key, movie searches show direct links to JustWatch and each service. TMDB lists a film's original language, not every dub, so for dubbed versions check the title's Audio menu in the app. This product uses the TMDB API but is not endorsed or certified by TMDB.
- **Clear answers for paid streaming services.** Netflix, Prime Video, JioHotstar, Disney+, SonyLIV, ZEE5 and similar links are recognised up front: BDM explains that they are DRM-protected and points you to the service's own offline downloads, instead of failing halfway.
- **Light and dark themes,** a desktop sidebar and phone navigation, all from one codebase.

## Install

- **Windows 10/11:** [BDM-Setup.exe](https://github.com/mycodeclub/BpstDownloadManager/releases/latest/download/BDM-Setup.exe). Per-user install, no admin rights needed, and it upgrades BpstVideoDownloader / VeloxDM 0.x and keeps your downloads and settings.
- **Android 8+:** [BDM-android.apk](https://github.com/mycodeclub/BpstDownloadManager/releases/latest/download/BDM-android.apk). Allow "Install unknown apps" for your browser. Files are saved to **Download/BDM**.

### Updating

Install the new version over the old one; you don't need to uninstall first.

- **Windows:** run the new `BDM-Setup.exe`. Setup detects the installed BDM (also VeloxDM / BpstVideoDownloader 0.x), shows *"Update BDM from x to y"*, closes BDM if it is running, installs into the same folder with the same options, and keeps your download list, settings and video tools (`%LOCALAPPDATA%\BDM`). It warns before replacing a newer version with an older one. Unattended: `BDM-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART`.
- **Android:** open the new `BDM-android.apk` and tap **Update**. Every release has the same package id (`com.bitprosofttech.bdm`), a higher version code and is signed with the BitProSoftTech release key, so Android installs it over the old one and keeps your downloads and settings.
  - **If you have 2.0.0:** that release was signed with a development key, so Android may say *"App not installed"* / *"package conflicts with an existing package"* for 2.1. Uninstall BDM 2.0 once and install the new APK (files already in Download/BDM stay; the download list and settings start fresh). Later releases update normally.

See [CHANGELOG.md](CHANGELOG.md) for what changed in each version.

Builds are not code-signed yet. On Windows, SmartScreen may say "Windows protected your PC": click **More info → Run anyway**. Each release lists SHA-256 checksums.

## All versions

Every release stays on GitHub, so you can always get the latest or go back to an earlier one. The links below always point to that exact version; [releases/latest](https://github.com/mycodeclub/BpstDownloadManager/releases/latest) is always the newest.

| Version | Date | Windows | Android | Notes |
|---|---|---|---|---|
| [2.2.0](https://github.com/mycodeclub/BpstDownloadManager/releases/tag/v2.2.0) | 2026-11-28 | [BDM-Setup-2.2.0.exe](https://github.com/mycodeclub/BpstDownloadManager/releases/download/v2.2.0/BDM-Setup-2.2.0.exe) | [BDM-2.2.0-android.apk](https://github.com/mycodeclub/BpstDownloadManager/releases/download/v2.2.0/BDM-2.2.0-android.apk) | **Latest.** Movies & web series in Smart Finder (where to watch, by language), more legal game sources |
| [2.1.1](https://github.com/mycodeclub/BpstDownloadManager/releases/tag/v2.1.1) | 2026-11-28 | [BDM-Setup-2.1.1.exe](https://github.com/mycodeclub/BpstDownloadManager/releases/download/v2.1.1/BDM-Setup-2.1.1.exe) | [BDM-2.1.1-android.apk](https://github.com/mycodeclub/BpstDownloadManager/releases/download/v2.1.1/BDM-2.1.1-android.apk) | Setup updates an installed BDM in place |
| [2.1.0](https://github.com/mycodeclub/BpstDownloadManager/releases/tag/v2.1.0) | 2026-11-22 | [BDM-Setup-2.1.0.exe](https://github.com/mycodeclub/BpstDownloadManager/releases/download/v2.1.0/BDM-Setup-2.1.0.exe) | [BDM-2.1.0-android.apk](https://github.com/mycodeclub/BpstDownloadManager/releases/download/v2.1.0/BDM-2.1.0-android.apk) | Smart Finder, Facebook/Instagram sign-in |
| [2.0.0](https://github.com/mycodeclub/BpstDownloadManager/releases/tag/v2.0.0) | 2026-11-21 | [BDM-Setup-2.0.0.exe](https://github.com/mycodeclub/BpstDownloadManager/releases/download/v2.0.0/BDM-Setup-2.0.0.exe) | [BDM-2.0.0-android.apk](https://github.com/mycodeclub/BpstDownloadManager/releases/download/v2.0.0/BDM-2.0.0-android.apk) | New Windows + Android app (dev-signed APK, see Updating) |
| [0.1.0](https://github.com/mycodeclub/BpstDownloadManager/releases/tag/v0.1.0) | 2026-11-20 | [BpstVideoDownloader-Setup-0.1.0.exe](https://github.com/mycodeclub/BpstDownloadManager/releases/download/v0.1.0/BpstVideoDownloader-Setup-0.1.0.exe) | – | First release (WPF, Windows only) |

To install an older version over a newer one on Windows, Setup asks first. On Android, uninstall the newer version first (Android never installs an older version over a newer one).

## Build

Requirements: Windows 10/11, the [.NET 10 SDK](https://dotnet.microsoft.com/download) with the MAUI workloads (`dotnet workload install maui-windows maui-android`), the Android SDK and a JDK 17+ (both come with Visual Studio's ".NET MAUI" workload).

```powershell
git clone https://github.com/mycodeclub/BpstDownloadManager.git
cd BpstDownloadManager
dotnet test tests/Bdm.Core.Tests
dotnet build src/Bdm.App -f net10.0-windows10.0.19041.0     # Windows
dotnet build src/Bdm.App -f net10.0-android                  # Android
.\build-installer.ps1 -Version 2.2.0                          # dist\BDM-Setup-2.2.0.exe + BDM-2.2.0-android.apk
```

### Releasing an update

1. Bump the version: `ApplicationDisplayVersion` / `ApplicationVersion` in `src/Bdm.App/Bdm.App.csproj`, `Version` in `Directory.Build.props`, `AppVersion` in `installer/BDM.iss`, the default in `build-installer.ps1` and `.github/workflows/build.yml`, add the release to `CHANGELOG.md`, and add its row to **All versions** in this README and on the website (`docs/index.html`).
2. `.\build-installer.ps1 -Version x.y.z`. It derives the Android version code from the version (`2.1.0 → 20100`, `2.2.0 → 20200`), so every APK can update the one before.
3. Tag and push (`git tag vx.y.z && git push origin main vx.y.z`). CI builds, signs and attaches both installers to the GitHub Release; the `releases/latest/download/...` links then serve the update.

**Android signing key.** Updates only install if every APK is signed with the same key. The release key lives outside the repo: locally in `%USERPROFILE%\BDM-signing\` (`bdm-release.keystore`, `keystore-password.txt`), in CI as the `BDM_KEYSTORE_BASE64` and `BDM_KEYSTORE_PASSWORD` repository secrets. Back it up: a lost key means users must uninstall to get updates. The build refuses to make a release APK without it (`-AllowDebugSigning` for test builds only).

**Optional: let 2.0.0 phones update in place.** If you still have the `debug.keystore` that built 2.0.0 (`%LOCALAPPDATA%\Xamarin\Mono for Android\debug.keystore` on that PC), run `tools\create-signing-lineage.ps1` once. It records a debug → release key rotation in `%USERPROFILE%\BDM-signing\bdm-lineage.bin`, and from then on `build-installer.ps1` signs APKs so that Android 9+ phones with 2.0.0 accept them as an update. (CI doesn't use the lineage yet; build the release APK locally if you need it.)

iOS is prepared (`src/Bdm.App/Platforms/iOS`) but not shipped. Build it on a Mac with `-p:EnableIos=true`.

Real-internet tests (YouTube download + merge, torrent swarm) are opt-in: set `BDM_NET_TESTS=1`.

## Project layout

| Path | Contents |
|---|---|
| `src/Bdm.Core` | Platform-neutral engine: `SegmentedDownloader`, `HlsDownloader`, `TorrentService` (MonoTorrent), `YoutubeExplodeBackend` + `MediaDownloader` (yt-dlp), `DownloadManager` (queue, Focus, limits, retries, persistence), `Finder/` (Smart Finder: store, free-download and where-to-watch sources, `SourceGuard` piracy filter) |
| `src/Bdm.App` | .NET MAUI app: pages, view models, `Platforms/Windows` (single instance, clipboard, keep-awake), `Platforms/Android` (foreground service, MediaMuxer, share intents), `Platforms/iOS` |
| `tests/Bdm.Core.Tests` | xUnit tests: segmented/HLS/AES/resume against a local test server, rate limiting, Focus, changed-file detection, torrents |
| `installer/BDM.iss` | Windows installer and updater (Inno Setup) |
| `build-installer.ps1` | Release build: Windows installer + signed Android APK |
| `tools/` | `make-icon.ps1` (app icon), `create-signing-lineage.ps1` (one-time Android key rotation) |
| `docs/` | Project website (GitHub Pages) |

App data: `%LOCALAPPDATA%\BDM` on Windows and app-private storage on Android. It holds the download list, settings, logs and, on Windows, the video tools. `BDM.exe --self-test` opens every page and exits with 0 if they all work.

## Roadmap

See [ROADMAP.md](ROADMAP.md): what's still missing in today's download managers, and what's planned for BDM.

## Responsible use

Only download content you have the right to download. Respect copyright and each site's terms. Smart Finder only lists official stores, official streaming services and legally free sources; known piracy sites are blocked (`SourceGuard`), and that is not configurable. DRM-protected streams (Netflix, Prime Video, JioHotstar, Disney+…) are not supported and won't be: circumventing DRM is illegal in most countries (for example under India's Copyright Act s.65A and the US DMCA). BDM uses MonoTorrent (MIT), YoutubeExplode (LGPL-3.0) and, on Windows, the separate programs yt-dlp, FFmpeg and Deno under their own licenses.

## License

[MIT](LICENSE)
