// Sends the visitor to the newest BDM installer in the latest GitHub release.
// Picks the highest-versioned file whose name matches the platform pattern, so it keeps working
// even if a release is missing the fixed-name copies (BDM-Setup.exe / BDM-android.apk).
(async () => {
  const repo = "mycodeclub/BpstDownloadManager";
  const kind = document.documentElement.dataset.kind; // "windows" | "android"
  const fallback = `https://github.com/${repo}/releases/latest`;
  const fixedName = kind === "android" ? "BDM-android.apk" : "BDM-Setup.exe";
  const pattern = kind === "android" ? /^BDM-(\d+)\.(\d+)\.(\d+)-android\.apk$/ : /^BDM-Setup-(\d+)\.(\d+)\.(\d+)\.exe$/;
  const status = document.getElementById("status");
  try {
    const r = await fetch(`https://api.github.com/repos/${repo}/releases/latest`, { headers: { Accept: "application/vnd.github+json" } });
    if (!r.ok) throw new Error("GitHub answered " + r.status);
    const release = await r.json();
    let best = null, bestKey = -1;
    for (const a of release.assets || []) {
      const m = pattern.exec(a.name);
      if (!m) continue;
      const key = (+m[1]) * 1e6 + (+m[2]) * 1e3 + (+m[3]);
      if (key > bestKey) { best = a; bestKey = key; }
    }
    best ??= (release.assets || []).find(a => a.name === fixedName);
    if (!best) throw new Error("no installer in the latest release");
    status.textContent = `Downloading ${best.name} (BDM ${release.tag_name})…`;
    location.replace(best.browser_download_url);
  } catch (e) {
    status.textContent = "Opening the latest release…";
    location.replace(fallback);
  }
})();
