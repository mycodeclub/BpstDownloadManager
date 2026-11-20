using System;
using System.IO;
using System.Threading;

namespace BpstVideoDownloader.App.Services;

/// <summary>
/// Small text log in %LOCALAPPDATA%\BpstVideoDownloader\logs (startup steps and crashes),
/// so problems on users' PCs can be diagnosed. Keeps the last 5 days.
/// </summary>
public static class AppLog
{
    private static readonly object Lock = new();
    private static readonly string Dir = Path.Combine(Core.DownloadManager.DefaultDataDir, "logs");

    public static string FilePath => Path.Combine(Dir, $"app-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:HH:mm:ss.fff} [{level}] [pid {Environment.ProcessId} t{Thread.CurrentThread.ManagedThreadId}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception) { /* logging must never break the app */ }
    }

    public static void Cleanup()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Dir, "app-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-5)) File.Delete(f);
        }
        catch (Exception) { }
    }
}
