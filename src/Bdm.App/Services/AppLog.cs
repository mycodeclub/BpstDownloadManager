namespace Bdm.App.Services;

/// <summary>Text log in the app data folder (logs/app-yyyy-MM-dd.log): startup steps and crashes. Keeps 5 days.</summary>
public static class AppLog
{
    private static readonly object Lock = new();
    private static string _dir = Path.Combine(Path.GetTempPath(), "BDM", "logs");

    public static void Initialize(string dataDirectory)
    {
        _dir = Path.Combine(dataDirectory, "logs");
        try
        {
            Directory.CreateDirectory(_dir);
            foreach (var f in Directory.EnumerateFiles(_dir, "app-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-5)) File.Delete(f);
        }
        catch (Exception) { }
    }

    public static string FilePath => Path.Combine(_dir, $"app-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
#if ANDROID
        // Also to logcat ("adb logcat -s BDM"), since app files aren't readable on release builds.
        if (level == "ERROR") Android.Util.Log.Error("BDM", message); else Android.Util.Log.Info("BDM", message);
#endif
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(_dir);
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception) { }
    }
}
