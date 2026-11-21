# BDM roadmap

## Where today's download managers fall short, and what BDM does about it

| Gap in the market | Typical today | BDM |
|---|---|---|
| **One app for files, videos and torrents** | Separate tools: a download manager, a YouTube downloader and a torrent client | ✅ 2.0: one queue for HTTP, YouTube, streams and torrents |
| **Same app on PC and phone** | Desktop-only, or weak mobile versions | ✅ 2.0: Windows and Android from one codebase, iOS prepared |
| **Prioritise one download** | Only global limits | ✅ 2.0: ⚡ Focus mode (others slowed or paused, restored automatically) |
| **`blob:` / streaming video** | Needs a browser extension or DevTools | ✅ 2.0: built-in Video Grabber, also on Android |
| **YouTube breaking every few weeks** | Users wait for an app update | ✅ yt-dlp auto-updates daily (Windows); built-in fallback |
| **Silent corruption when the server file changes** | Resume glues two versions together | ✅ 2.0: If-Range detection, restarts cleanly |
| **Ads, bundled offers, paid upgrades** | Common | ✅ Free and open source (MIT), no telemetry |
| **Browser integration without a paid extension** | Closed extensions | ⏳ planned: open-source Chrome/Edge/Firefox extension |
| **Sync between PC and phone** | Rare | ⏳ planned: send a link from phone to PC (and back) on the same network |
| **Verify downloads** | Manual hashing | ⏳ planned: paste a SHA-256/MD5 and BDM checks it |
| **Scheduling** | Basic | ⏳ planned: download windows ("only at night", "only on Wi-Fi", "pause on metered") |

## Next releases

### 2.1
- Browser extension (Chromium + Firefox): send links, catch downloads and detected video streams to BDM (native messaging / localhost bridge)
- Checksum verification (SHA-256/SHA-1/MD5) on completion
- Scheduler: time windows, Wi-Fi only (Android), pause on metered connections
- Notification-area (tray) icon on Windows
- Torrent file picker for magnet links after metadata arrives

### 2.2
- Phone ↔ PC hand-off over the local network (QR pairing)
- Playlist/channel downloads with per-item quality
- Download categories with per-category folders and rules
- Built-in media preview while downloading (progressive MP4, torrent streaming)

### Later
- iOS release (background URLSession for long downloads, AVFoundation merging)
- Code-signed Windows builds and Play Store listing (store edition without YouTube; full edition from bitprosofttech.com)
- Translations
