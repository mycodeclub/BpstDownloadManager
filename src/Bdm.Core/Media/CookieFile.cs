using System.Globalization;
using System.Text;

namespace Bdm.Core.Media;

public sealed record CookieEntry(string Domain, string Path, string Name, string Value, bool Secure, bool HttpOnly, DateTime? ExpiresUtc);

/// <summary>Writes cookies in the Netscape cookies.txt format read by yt-dlp's --cookies option.</summary>
public static class CookieFile
{
    public static string ToNetscape(IEnumerable<CookieEntry> cookies)
    {
        var sb = new StringBuilder("# Netscape HTTP Cookie File\n# Written by BpstVideoDownloader. Contains your sign-in: do not share this file.\n\n");
        foreach (var c in cookies)
        {
            if (string.IsNullOrEmpty(c.Domain) || c.Name.Contains('\t') || c.Value.Contains('\t') || c.Value.Contains('\n')) continue;
            bool subdomains = c.Domain.StartsWith('.');
            long expires = c.ExpiresUtc is { } e ? new DateTimeOffset(DateTime.SpecifyKind(e, DateTimeKind.Utc)).ToUnixTimeSeconds() : 0;
            sb.Append(c.HttpOnly ? "#HttpOnly_" : "").Append(c.Domain).Append('\t')
              .Append(subdomains ? "TRUE" : "FALSE").Append('\t')
              .Append(string.IsNullOrEmpty(c.Path) ? "/" : c.Path).Append('\t')
              .Append(c.Secure ? "TRUE" : "FALSE").Append('\t')
              .Append(expires.ToString(CultureInfo.InvariantCulture)).Append('\t')
              .Append(c.Name).Append('\t').Append(c.Value).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Reads a Netscape cookies.txt into <see cref="System.Net.Cookie"/>s.</summary>
    public static IReadOnlyList<System.Net.Cookie> Read(string path)
    {
        var list = new List<System.Net.Cookie>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.StartsWith("#HttpOnly_", StringComparison.Ordinal) ? raw["#HttpOnly_".Length..] : raw;
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var p = line.Split('\t');
            if (p.Length < 7) continue;
            try
            {
                var c = new System.Net.Cookie(p[5], p[6], p[2], p[0]) { Secure = p[3] == "TRUE", HttpOnly = raw.StartsWith("#HttpOnly_", StringComparison.Ordinal) };
                if (long.TryParse(p[4], out var exp) && exp > 0) c.Expires = DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime;
                list.Add(c);
            }
            catch (System.Net.CookieException) { }
        }
        return list;
    }

    public static void Write(string path, IEnumerable<CookieEntry> cookies)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, ToNetscape(cookies));
    }
}

/// <summary>Raised when a site needs a signed-in account (age-restricted, private, members-only, bot check).</summary>
public sealed class MediaSignInRequiredException(string message) : Exception(message);
