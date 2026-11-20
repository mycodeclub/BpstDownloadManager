namespace BpstVideoDownloader.Core.Http;

public static class FileNameHelper
{
    private static readonly Dictionary<string, string> ExtensionsByMediaType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["video/mp4"] = ".mp4", ["video/webm"] = ".webm", ["video/x-matroska"] = ".mkv", ["video/quicktime"] = ".mov",
        ["video/x-msvideo"] = ".avi", ["video/mp2t"] = ".ts", ["audio/mpeg"] = ".mp3", ["audio/mp4"] = ".m4a",
        ["audio/ogg"] = ".ogg", ["audio/wav"] = ".wav", ["audio/flac"] = ".flac", ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png", ["image/gif"] = ".gif", ["image/webp"] = ".webp", ["application/pdf"] = ".pdf",
        ["application/zip"] = ".zip", ["application/x-zip-compressed"] = ".zip", ["application/x-7z-compressed"] = ".7z",
        ["application/vnd.rar"] = ".rar", ["application/x-rar-compressed"] = ".rar", ["application/gzip"] = ".gz",
        ["application/x-msdownload"] = ".exe", ["application/x-msi"] = ".msi", ["application/x-iso9660-image"] = ".iso",
        ["text/plain"] = ".txt", ["application/json"] = ".json",
    };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string FromResponse(HttpResponseMessage response, Uri uri)
    {
        var cd = response.Content.Headers.ContentDisposition;
        string? name = cd?.FileNameStar ?? cd?.FileName;
        name = name?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(name)) name = FromUrl(uri);

        if (!Path.HasExtension(name) &&
            response.Content.Headers.ContentType?.MediaType is { } mt &&
            ExtensionsByMediaType.TryGetValue(mt, out var ext))
        {
            name += ext;
        }
        return Sanitize(name);
    }

    public static string FromUrl(Uri uri)
    {
        var last = uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? "";
        try { last = Uri.UnescapeDataString(last); } catch (UriFormatException) { }
        return string.IsNullOrWhiteSpace(last) ? "download" : Sanitize(last);
    }

    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray();
        var clean = new string(chars).Trim().TrimEnd('.', ' ');
        if (clean.Length == 0) clean = "download";

        var stem = Path.GetFileNameWithoutExtension(clean);
        var ext = Path.GetExtension(clean);
        if (ReservedNames.Contains(stem)) stem = "_" + stem;
        if (ext.Length > 16) { stem += ext; ext = ""; }
        if (stem.Length > 180) stem = stem[..180];
        return stem + ext;
    }

    /// <summary>Returns a name in <paramref name="folder"/> that is not taken by a file or a partial download.</summary>
    public static string GetUniqueFileName(string folder, string fileName, Func<string, bool>? isReserved = null)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var candidate = fileName;
        for (int i = 1; IsTaken(candidate); i++) candidate = $"{stem} ({i}){ext}";
        return candidate;

        bool IsTaken(string n)
        {
            var p = Path.Combine(folder, n);
            return File.Exists(p) || File.Exists(p + ".vdmpart") || (isReserved?.Invoke(p) ?? false);
        }
    }
}
