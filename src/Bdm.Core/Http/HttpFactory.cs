using System.Net;
using System.Net.Sockets;

namespace Bdm.Core.Http;

public static class HttpFactory
{
    // A mainstream browser UA: many CDNs refuse or throttle unknown clients.
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";

    public static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            // Never decompress: byte ranges must map 1:1 to the file on disk.
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            MaxConnectionsPerServer = 64,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            UseCookies = false,
            ConnectCallback = ConnectIPv4FirstAsync,
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>
    /// Many networks publish IPv6 (or NAT64) addresses that don't actually route. .NET tries them first
    /// and only fails after the connect timeout. Try IPv4 first, then IPv6, with a short timeout for each address.
    /// </summary>
    private static async ValueTask<Stream> ConnectIPv4FirstAsync(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
        var ordered = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Concat(addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6));

        Exception? last = null;
        foreach (var address in ordered)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(TimeSpan.FromSeconds(6));
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, ctx.DnsEndPoint.Port), attempt.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                socket.Dispose();
                last = ex is OperationCanceledException ? new TimeoutException($"Timed out connecting to {address}.") : ex;
            }
        }
        throw new HttpRequestException($"Could not connect to {ctx.DnsEndPoint.Host}: {last?.Message ?? "no addresses"}", last);
    }

    public static HttpRequestMessage CreateRequest(DownloadJob job) => CreateRequest(job.Url, job.Referrer, job.Headers);

    public static HttpRequestMessage CreateRequest(string url, string? referrer, IReadOnlyDictionary<string, string>? headers = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (headers is not null)
            foreach (var (name, value) in headers)
                req.Headers.TryAddWithoutValidation(name, value);
        if (!req.Headers.Contains("User-Agent")) req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        if (!string.IsNullOrWhiteSpace(referrer) && Uri.TryCreate(referrer, UriKind.Absolute, out var r))
            req.Headers.Referrer = r;
        return req;
    }
}
