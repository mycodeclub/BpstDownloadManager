using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace VeloxDM.Core.Media;

public sealed record MediaInfo(string Title, string? Duration, string? Site);

/// <summary>Runs yt-dlp for video sites (YouTube etc.) and HLS/DASH streams.</summary>
public sealed class MediaDownloader
{
    private const string ProgressTag = "VDMPROG";
    private const string TitleTag = "VDMTITLE";
    private const string FileTag = "VDMFILE";

    private readonly MediaTools _tools;

    public MediaDownloader(MediaTools tools) => _tools = tools;

    /// <summary>Pass --force-ipv4 to yt-dlp (networks with broken IPv6 otherwise stall).</summary>
    public bool ForceIPv4 { get; set; } = true;

    /// <summary>cookies.txt with the user's sign-in (takes priority over <see cref="CookiesFromBrowser"/>).</summary>
    public string? CookiesFile { get; set; }

    /// <summary>Browser name for yt-dlp's --cookies-from-browser (firefox, chrome, edge, brave...).</summary>
    public string? CookiesFromBrowser { get; set; }

    public async Task<MediaInfo> ProbeAsync(string url, CancellationToken ct)
    {
        var args = new List<string> { "--no-playlist", "--no-warnings", "--skip-download",
            "--print", "%(title)s", "--print", "%(duration_string)s", "--print", "%(extractor_key)s", "--", url };
        var lines = new List<string>();
        var errors = new List<string>();
        int code = await RunAsync(args, lines.Add, errors.Add, ct);
        if (code != 0 || lines.Count == 0)
            throw Translate(LastError(errors) ?? "yt-dlp could not read this URL.");
        string? Na(int i) => i < lines.Count && lines[i] != "NA" ? lines[i] : null;
        return new MediaInfo(Na(0) ?? "video", Na(1), Na(2));
    }

    public async Task DownloadAsync(DownloadJob job, int connections, CancellationToken ct)
    {
        Directory.CreateDirectory(job.Folder);
        job.Status = DownloadStatus.Connecting;
        job.Stage = "Fetching video info";
        var resultFile = Path.Combine(Path.GetTempPath(), $"vdm-{job.Id:N}.txt");
        TryDelete(resultFile);

        var args = new List<string>
        {
            "--no-playlist", "--newline", "--no-colors", "--no-mtime",
            "--concurrent-fragments", Math.Clamp(connections, 1, 32).ToString(CultureInfo.InvariantCulture),
            "--progress-template", $"download:{ProgressTag} %(progress.downloaded_bytes)s %(progress.total_bytes)s %(progress.total_bytes_estimate)s",
            // --print would switch yt-dlp to quiet mode; --print-to-file keeps normal output.
            "--print-to-file", $"{TitleTag} %(title)s", resultFile,
            "--print-to-file", $"after_move:{FileTag} %(filepath)s", resultFile,
            "-P", job.Folder,
            "-o", "%(title).150B [%(id)s].%(ext)s",
        };
        args.AddRange(MediaQuality.BuildArgs(job.MediaQuality, _tools.FfmpegPath is not null));
        if (!string.IsNullOrWhiteSpace(job.Referrer)) args.AddRange(["--referer", job.Referrer]);
        args.AddRange(["--", job.Url]);

        int part = 0;
        string? finalFile = null;
        var errors = new List<string>();

        void OnLine(string line)
        {
            if (line.StartsWith(ProgressTag, StringComparison.Ordinal))
            {
                var p = line.Split(' ');
                if (p.Length < 4) return;
                long? done = ParseLong(p[1]);
                long? total = ParseLong(p[2]) ?? ParseLong(p[3]);
                if (done is long d) job.MediaDownloaded = d;
                if (total is long t && t > 0) job.TotalBytes = t;
                job.Status = DownloadStatus.Downloading;
            }
            else if (line.StartsWith("[download] Destination:", StringComparison.Ordinal))
            {
                part++;
                job.MediaDownloaded = 0;
                job.Stage = part > 1 ? $"Downloading stream {part}" : "Downloading";
            }
            else if (line.StartsWith("[Merger]", StringComparison.Ordinal) || line.StartsWith("[VideoConvertor]", StringComparison.Ordinal))
            {
                job.Stage = "Merging video and audio";
            }
            else if (line.StartsWith("[ExtractAudio]", StringComparison.Ordinal))
            {
                job.Stage = "Converting audio";
            }
        }

        int code = await RunAsync(args, OnLine, errors.Add, ct);
        ct.ThrowIfCancellationRequested();
        if (code != 0)
            throw Translate(LastError(errors) ?? $"yt-dlp exited with code {code}.");

        if (File.Exists(resultFile))
        {
            foreach (var line in await File.ReadAllLinesAsync(resultFile, Encoding.UTF8, CancellationToken.None))
            {
                if (line.StartsWith(FileTag, StringComparison.Ordinal)) finalFile = line[(FileTag.Length + 1)..].Trim();
                else if (line.StartsWith(TitleTag, StringComparison.Ordinal) && finalFile is null) job.FileName = line[(TitleTag.Length + 1)..];
            }
            TryDelete(resultFile);
        }

        if (finalFile is not null)
        {
            job.Folder = Path.GetDirectoryName(finalFile) ?? job.Folder;
            job.FileName = Path.GetFileName(finalFile);
            try { job.TotalBytes = new FileInfo(finalFile).Length; job.MediaDownloaded = job.TotalBytes.Value; } catch (IOException) { }
        }
        job.Stage = null;
    }

    private async Task<int> RunAsync(IEnumerable<string> args, Action<string> onStdout, Action<string> onStderr, CancellationToken ct)
    {
        var exe = _tools.YtDlpPath ?? throw new InvalidOperationException("yt-dlp is not installed. Open Video Tools to install it.");
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        var prefix = new List<string> { "--encoding", "utf-8" };
        if (ForceIPv4) prefix.Add("--force-ipv4");
        // yt-dlp only enables Deno by default; fall back to an installed Node.js.
        if (_tools.DenoPath is null && _tools.NodePath is { } node) prefix.AddRange(["--js-runtimes", "node:" + node]);
        if (!string.IsNullOrWhiteSpace(CookiesFile) && File.Exists(CookiesFile)) prefix.AddRange(["--cookies", CookiesFile]);
        else if (!string.IsNullOrWhiteSpace(CookiesFromBrowser)) prefix.AddRange(["--cookies-from-browser", CookiesFromBrowser]);
        for (int i = 0; i < prefix.Count; i++) psi.ArgumentList.Insert(i, prefix[i]);
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        // Put the tools folder first on PATH so yt-dlp finds our ffmpeg and deno.
        psi.Environment["PATH"] = _tools.ToolsDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) onStdout(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) onStderr(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await proc.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        proc.WaitForExit(); // flush async output handlers
        return proc.ExitCode;
    }

    private static string? LastError(List<string> stderr)
    {
        var err = stderr.LastOrDefault(l => l.StartsWith("ERROR:", StringComparison.Ordinal)) ?? stderr.LastOrDefault(l => l.Trim().Length > 0);
        return err?.Replace("ERROR: ", "").Trim();
    }

    /// <summary>Turns yt-dlp's technical errors into messages that tell the user what to do.</summary>
    internal Exception Translate(string error)
    {
        bool signedIn = (!string.IsNullOrWhiteSpace(CookiesFile) && File.Exists(CookiesFile)) || !string.IsNullOrWhiteSpace(CookiesFromBrowser);

        if (error.Contains("Failed to decrypt with DPAPI", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("app-bound", StringComparison.OrdinalIgnoreCase))
            return new MediaSignInRequiredException(
                "Chrome and Edge lock their cookies so other apps can't read them. Use Settings → \"Sign in to YouTube\" in VeloxDM instead.");

        if (error.Contains("Could not copy", StringComparison.OrdinalIgnoreCase) && error.Contains("cookie", StringComparison.OrdinalIgnoreCase))
            return new MediaSignInRequiredException(
                "The browser is open and locking its cookies. Close it and retry, or use Settings → \"Sign in to YouTube\".");

        if (error.Contains("Sign in to confirm your age", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("age-restricted", StringComparison.OrdinalIgnoreCase))
            return new MediaSignInRequiredException(signedIn
                ? "Age-restricted video: the signed-in account isn't age-verified, or the sign-in expired. Sign in to YouTube again with an 18+ account."
                : "Age-restricted video: YouTube only shows it to a signed-in 18+ account. Click \"Sign in to YouTube\" (in Settings) once, then Resume.");

        if (error.Contains("not a bot", StringComparison.OrdinalIgnoreCase))
            return new MediaSignInRequiredException(
                "YouTube is asking to confirm you're not a bot. Sign in to YouTube in VeloxDM (Settings), then Resume.");

        if (error.Contains("Private video", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("members-only", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Join this channel", StringComparison.OrdinalIgnoreCase))
            return new MediaSignInRequiredException(
                "This video is private or for channel members only. Sign in to YouTube (Settings) with an account that has access.");

        if (error.Contains("DRM", StringComparison.Ordinal))
            return new InvalidOperationException("This video is DRM-protected (copy-protected), so it can't be downloaded.");

        if (error.Contains("--cookies", StringComparison.Ordinal))
            return new MediaSignInRequiredException("This site needs you to be signed in. Use Settings → sign-in options, then Resume.");

        return new InvalidOperationException(error);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static long? ParseLong(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (long)d : null;
}
