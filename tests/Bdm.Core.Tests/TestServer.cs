using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bdm.Core.Tests;

/// <summary>Local HTTP server that serves a random payload slowly, with or without byte-range support.</summary>
public sealed class TestServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    public byte[] Payload { get; }
    public string BaseUrl { get; }
    public int MaxConcurrent;
    private int _current;

    private TestServer(WebApplication app, byte[] payload, string baseUrl)
    {
        _app = app;
        Payload = payload;
        BaseUrl = baseUrl;
    }

    public static async Task<TestServer> StartAsync(int size, int chunkDelayMs = 2)
    {
        var payload = new byte[size];
        new Random(42).NextBytes(payload);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        TestServer? self = null;

        async Task Serve(HttpContext ctx, bool ranges)
        {
            int now = Interlocked.Increment(ref self!._current);
            InterlockedMax(ref self.MaxConcurrent, now);
            try
            {
                var data = self.CurrentPayload;
                long start = 0, end = data.Length - 1;
                var range = ctx.Request.Headers.Range.ToString();
                var etag = $"\"v{self.Version}\"";
                ctx.Response.ContentType = "application/octet-stream";
                ctx.Response.Headers.ContentDisposition = "attachment; filename=\"payload.bin\"";
                if (ranges) ctx.Response.Headers.ETag = etag;
                // If-Range with a stale validator: send the whole (new) file instead of the range.
                var ifRange = ctx.Request.Headers.IfRange.ToString();
                if (ifRange.Length > 0 && ifRange != etag) range = "";
                if (ranges && range.StartsWith("bytes="))
                {
                    var parts = range[6..].Split('-');
                    start = long.Parse(parts[0]);
                    if (parts[1].Length > 0) end = Math.Min(end, long.Parse(parts[1]));
                    ctx.Response.StatusCode = 206;
                    ctx.Response.Headers.ContentRange = $"bytes {start}-{end}/{data.Length}";
                }
                if (ranges) ctx.Response.Headers.AcceptRanges = "bytes";
                ctx.Response.ContentLength = end - start + 1;

                for (long p = start; p <= end; p += 16384)
                {
                    int n = (int)Math.Min(16384, end - p + 1);
                    await ctx.Response.Body.WriteAsync(data.AsMemory((int)p, n), ctx.RequestAborted);
                    if (chunkDelayMs > 0) await Task.Delay(chunkDelayMs, ctx.RequestAborted);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                Interlocked.Decrement(ref self._current);
            }
        }

        app.MapGet("/ranged", ctx => Serve(ctx, true));
        app.MapGet("/plain", ctx => Serve(ctx, false));

        // HLS: master playlist with two qualities, AES-128 encrypted MPEG-TS segments (IV from sequence number).
        app.MapGet("/hls/master.m3u8", ctx => ctx.Response.WriteAsync(
            "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360\nlow.m3u8\n" +
            "#EXT-X-STREAM-INF:BANDWIDTH=2500000,RESOLUTION=1280x720\nmedia.m3u8\n"));
        app.MapGet("/hls/low.m3u8", ctx => { ctx.Response.StatusCode = 500; return Task.CompletedTask; }); // must not be picked
        app.MapGet("/hls/media.m3u8", ctx =>
        {
            var sb = new System.Text.StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:4\n#EXT-X-MEDIA-SEQUENCE:7\n#EXT-X-KEY:METHOD=AES-128,URI=\"key.bin\"\n");
            for (int i = 0; i < HlsSegments.Length; i++) sb.Append("#EXTINF:4.0,\nseg").Append(i).Append(".ts\n");
            sb.Append("#EXT-X-ENDLIST\n");
            return ctx.Response.WriteAsync(sb.ToString());
        });
        app.MapGet("/hls/key.bin", ctx => ctx.Response.Body.WriteAsync(HlsKey).AsTask());
        app.MapGet("/hls/seg{i:int}.ts", async (HttpContext ctx, int i) =>
        {
            if (chunkDelayMs > 0) await Task.Delay(chunkDelayMs * 5);
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Key = HlsKey;
            var iv = new byte[16];
            long seq = 7 + i;
            for (int b = 15; b >= 8; b--) { iv[b] = (byte)(seq & 0xFF); seq >>= 8; }
            var enc = aes.EncryptCbc(HlsSegments[i], iv, System.Security.Cryptography.PaddingMode.PKCS7);
            await ctx.Response.Body.WriteAsync(enc);
        });

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        self = new TestServer(app, payload, address);
        return self;
    }

    /// <summary>Bumping the version changes the file content and its ETag ("the file changed on the server").</summary>
    public int Version { get; set; } = 1;

    public byte[] CurrentPayload
    {
        get
        {
            if (Version == 1) return Payload;
            var p = new byte[Payload.Length];
            new Random(42 + Version).NextBytes(p);
            return p;
        }
    }

    public static readonly byte[] HlsKey = Enumerable.Range(1, 16).Select(i => (byte)(i * 7)).ToArray();

    public static readonly byte[][] HlsSegments = Enumerable.Range(0, 12).Select(i =>
    {
        var b = new byte[40_000 + i * 1000];
        new Random(100 + i).NextBytes(b);
        return b;
    }).ToArray();

    private static void InterlockedMax(ref int target, int value)
    {
        int cur;
        while (value > (cur = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, cur) != cur) { }
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

