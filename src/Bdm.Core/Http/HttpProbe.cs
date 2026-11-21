using System.Net;
using System.Net.Http.Headers;

namespace Bdm.Core.Http;

public sealed record ProbeResult(Uri FinalUri, string FileName, long? TotalBytes, bool SupportsRanges, string? ContentType)
{
    /// <summary>Strong ETag (weak ones can't be used with If-Range).</summary>
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }

    public bool LooksLikeWebPage =>
        ContentType is { } ct && (ct.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) ||
                                  ct.StartsWith("application/xhtml", StringComparison.OrdinalIgnoreCase));
}

public static class HttpProbe
{
    /// <summary>
    /// Requests the first byte range to learn the size, file name and whether the server
    /// accepts byte ranges (required for multi-connection downloads and resume).
    /// </summary>
    public static async Task<ProbeResult> ProbeAsync(HttpClient http, string url, string? referrer, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        using var req = HttpFactory.CreateRequest(url, referrer, headers);
        req.Headers.Range = new RangeHeaderValue(0, null);
        var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

        if (!resp.IsSuccessStatusCode)
        {
            // Some servers reject Range on empty files (416) or dislike ranged probes; retry plainly.
            resp.Dispose();
            using var plain = HttpFactory.CreateRequest(url, referrer, headers);
            resp = await http.SendAsync(plain, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!resp.IsSuccessStatusCode)
            {
                var code = resp.StatusCode;
                resp.Dispose();
                throw new HttpRequestException($"Server returned HTTP {(int)code} ({code}).", null, code);
            }
        }

        using (resp)
        {
            var finalUri = resp.RequestMessage?.RequestUri ?? new Uri(url);
            long? total;
            bool ranges = false;
            if (resp.StatusCode == HttpStatusCode.PartialContent && resp.Content.Headers.ContentRange is { HasLength: true } cr)
            {
                total = cr.Length;
                ranges = true;
            }
            else
            {
                total = resp.Content.Headers.ContentLength;
            }

            return new ProbeResult(finalUri, FileNameHelper.FromResponse(resp, finalUri), total, ranges,
                resp.Content.Headers.ContentType?.MediaType)
            {
                ETag = resp.Headers.ETag is { IsWeak: false } et ? et.Tag : null,
                LastModified = resp.Content.Headers.LastModified,
            };
        }
    }
}
