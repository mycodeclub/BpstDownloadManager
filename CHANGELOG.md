# Changelog

All BDM releases. Downloads: [GitHub Releases](https://github.com/mycodeclub/BpstDownloadManager/releases).

## [2.2.0](https://github.com/mycodeclub/BpstDownloadManager/releases/tag/v2.2.0) (2026-11-28)
- ✨ **Movies and web series in Smart Finder:** new Movies and Web series & TV categories show where a title streams legally in your country (Netflix, Prime Video, JioHotstar, ZEE5, SonyLIV, Apple TV…), where to rent or buy it, and official free streams.
- **Language filter** for movies and series (Hindi, English, Tamil, Telugu, Malayalam, Kannada, Bengali, Marathi, Punjabi, Gujarati, Korean, Japanese, Spanish, French); typing "in Hindi" or "Tamil dubbed" picks it too. Searches like "Mirzapur season 2 web series" are cleaned up to the title.
- Optional TMDB API key (Settings → Smart Finder) for exact per-country availability, data by JustWatch via TMDB; without it, direct links to JustWatch and each service.
- Public-domain films from the Internet Archive (only openly licensed or published 95+ years ago), with official torrents.
- More legal game sources: GOG search with prices and free DRM-free games, Humble Store, Game Jolt, IndieGala freebies.
- SourceGuard blocks more piracy sites: pirate movie/TV sites and YTS mirrors, cracked-game sites.

## [2.1.1](https://github.com/mycodeclub/BpstDownloadManager/releases/tag/v2.1.1) (2026-11-28)
- **Updates:** the Windows Setup now detects an installed BDM and works as an updater: "Update BDM from x to y" wording, same folder and options, closes the running app, warns before a downgrade (refuses it when run silently).
- **Android updates:** the version code is derived from the version number on every build, release APKs must be signed with the release key, and optional key rotation (`tools/create-signing-lineage.ps1`) lets phones with the debug-signed 2.0.0 update in place.
- Docs: "All versions" download table (README, website) with the latest and every earlier release; update instructions, release checklist, this changelog. Project metadata now says BDM instead of BpstVideoDownloader 0.1.0.

## [2.1.0](https://github.com/mycodeclub/BpstDownloadManager/releases/tag/v2.1.0) (2026-11-22)
- ✨ Smart Finder: search for games, software, e-books & PDFs, documents, courses and music from official stores, free legal downloads and official torrents, with category and Free / Paid filters. Piracy sites are filtered out.
- Facebook and Instagram sign-in for login-only posts; clearer sign-in wording.
- DRM streaming links (Netflix, Prime Video, JioHotstar, Disney+, SonyLIV, ZEE5…) are recognised and explained up front.
- Android APKs are signed with the BitProSoftTech release key (local key or CI secrets).

## [2.0.0](https://github.com/mycodeclub/BpstDownloadManager/releases/tag/v2.0.0) (2026-11-21)
- New app built with .NET MAUI for Windows and Android (iOS prepared), replacing the 0.x WPF app. The Windows installer upgrades VeloxDM / BpstVideoDownloader 0.x.
- Engine: up to 32 connections with resume, HLS with AES-128, torrents (MonoTorrent), Focus mode, speed limit, changed-file detection, retries for long downloads.
- YouTube: HD/4K video and audio downloaded in parallel and merged into MP4.
- Video Grabber for `blob:` / streaming pages, on Windows and Android.
- Android fixes from emulator testing, download problem logging; Windows completion toast fix, `--download=` command line.
- Note: the 2.0.0 APK was signed with a development key. To move to 2.1+, uninstall 2.0 once (unless the release was built with key rotation).

## [0.1.0](https://github.com/mycodeclub/BpstDownloadManager/releases/tag/v0.1.0) (2026-11-20)
- First open-source release as BpstVideoDownloader (WPF, Windows only): installer and portable exe.
