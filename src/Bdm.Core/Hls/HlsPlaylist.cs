using System.Globalization;

namespace Bdm.Core.Hls;

public sealed record HlsVariant(Uri Uri, long Bandwidth, int? Height, string? Resolution);

public sealed record HlsKey(string Method, Uri? Uri, byte[]? Iv);

public sealed record HlsSegment(int Index, Uri Uri, double Duration, HlsKey? Key, long MediaSequence, (long Length, long Offset)? ByteRange);

/// <summary>Parsed HLS playlist: either a master (list of qualities) or a media playlist (list of segments).</summary>
public sealed class HlsPlaylist
{
    public List<HlsVariant> Variants { get; } = new();
    public List<HlsSegment> Segments { get; } = new();
    /// <summary>fMP4 initialisation segment (EXT-X-MAP), written before the first segment.</summary>
    public Uri? InitSegment { get; private set; }
    public (long Length, long Offset)? InitByteRange { get; private set; }
    public bool IsMaster => Variants.Count > 0;
    public bool IsEndless { get; private set; } = true;

    public static HlsPlaylist Parse(string text, Uri baseUri)
    {
        var p = new HlsPlaylist();
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || !lines[0].StartsWith("#EXTM3U", StringComparison.Ordinal))
            throw new InvalidDataException("Not an HLS playlist (missing #EXTM3U).");

        long mediaSequence = 0;
        HlsKey? key = null;
        double? pendingDuration = null;
        (long, long)? pendingRange = null;
        long nextRangeOffset = 0;
        string? pendingVariant = null;
        int index = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
            {
                pendingVariant = line["#EXT-X-STREAM-INF:".Length..];
            }
            else if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.Ordinal))
            {
                long.TryParse(line["#EXT-X-MEDIA-SEQUENCE:".Length..], out mediaSequence);
            }
            else if (line.StartsWith("#EXT-X-KEY:", StringComparison.Ordinal))
            {
                var a = ParseAttributes(line["#EXT-X-KEY:".Length..]);
                var method = a.GetValueOrDefault("METHOD", "NONE");
                key = method == "NONE" ? null : new HlsKey(method,
                    a.TryGetValue("URI", out var ku) ? new Uri(baseUri, ku) : null,
                    a.TryGetValue("IV", out var iv) ? ParseHex(iv) : null);
            }
            else if (line.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal))
            {
                var a = ParseAttributes(line["#EXT-X-MAP:".Length..]);
                if (a.TryGetValue("URI", out var mu)) p.InitSegment = new Uri(baseUri, mu);
                if (a.TryGetValue("BYTERANGE", out var br)) p.InitByteRange = ParseByteRange(br, 0);
            }
            else if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            {
                var v = line["#EXTINF:".Length..].Split(',')[0];
                pendingDuration = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
            }
            else if (line.StartsWith("#EXT-X-BYTERANGE:", StringComparison.Ordinal))
            {
                var r = ParseByteRange(line["#EXT-X-BYTERANGE:".Length..], nextRangeOffset);
                pendingRange = r;
                nextRangeOffset = r.Offset + r.Length;
            }
            else if (line.StartsWith("#EXT-X-ENDLIST", StringComparison.Ordinal))
            {
                p.IsEndless = false;
            }
            else if (!line.StartsWith('#'))
            {
                var uri = new Uri(baseUri, line);
                if (pendingVariant is not null)
                {
                    var a = ParseAttributes(pendingVariant);
                    long.TryParse(a.GetValueOrDefault("BANDWIDTH", "0"), out var bw);
                    var res = a.GetValueOrDefault("RESOLUTION");
                    int? height = res?.Split('x') is [_, var h] && int.TryParse(h, out var hh) ? hh : null;
                    p.Variants.Add(new HlsVariant(uri, bw, height, res));
                    pendingVariant = null;
                }
                else if (pendingDuration is not null)
                {
                    p.Segments.Add(new HlsSegment(index, uri, pendingDuration.Value, key, mediaSequence + index, pendingRange));
                    index++;
                    pendingDuration = null;
                    pendingRange = null;
                }
            }
        }
        if (p.IsMaster) p.IsEndless = false;
        return p;
    }

    /// <summary>Best variant not taller than <paramref name="maxHeight"/> (null = best overall).</summary>
    public HlsVariant PickVariant(int? maxHeight)
    {
        var candidates = maxHeight is int m ? Variants.Where(v => v.Height is null || v.Height <= m).ToList() : Variants;
        if (candidates.Count == 0) candidates = Variants;
        return candidates.OrderByDescending(v => v.Height ?? 0).ThenByDescending(v => v.Bandwidth).First();
    }

    internal static Dictionary<string, string> ParseAttributes(string s)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        while (i < s.Length)
        {
            int eq = s.IndexOf('=', i);
            if (eq < 0) break;
            var name = s[i..eq].Trim();
            string value;
            if (eq + 1 < s.Length && s[eq + 1] == '"')
            {
                int close = s.IndexOf('"', eq + 2);
                if (close < 0) close = s.Length;
                value = s[(eq + 2)..close];
                i = s.IndexOf(',', close) is var c && c >= 0 ? c + 1 : s.Length;
            }
            else
            {
                int comma = s.IndexOf(',', eq);
                value = comma < 0 ? s[(eq + 1)..] : s[(eq + 1)..comma];
                i = comma < 0 ? s.Length : comma + 1;
            }
            result[name] = value.Trim();
        }
        return result;
    }

    private static (long Length, long Offset) ParseByteRange(string s, long defaultOffset)
    {
        var parts = s.Trim('"').Split('@');
        long len = long.Parse(parts[0], CultureInfo.InvariantCulture);
        long off = parts.Length > 1 ? long.Parse(parts[1], CultureInfo.InvariantCulture) : defaultOffset;
        return (len, off);
    }

    private static byte[] ParseHex(string s)
    {
        s = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
        return Convert.FromHexString(s.PadLeft(32, '0'));
    }
}
