using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace VeloxDM.Core.Tests;

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
                long start = 0, end = payload.Length - 1;
                var range = ctx.Request.Headers.Range.ToString();
                ctx.Response.ContentType = "application/octet-stream";
                ctx.Response.Headers.ContentDisposition = "attachment; filename=\"payload.bin\"";
                if (ranges && range.StartsWith("bytes="))
                {
                    var parts = range[6..].Split('-');
                    start = long.Parse(parts[0]);
                    if (parts[1].Length > 0) end = Math.Min(end, long.Parse(parts[1]));
                    ctx.Response.StatusCode = 206;
                    ctx.Response.Headers.ContentRange = $"bytes {start}-{end}/{payload.Length}";
                }
                if (ranges) ctx.Response.Headers.AcceptRanges = "bytes";
                ctx.Response.ContentLength = end - start + 1;

                for (long p = start; p <= end; p += 16384)
                {
                    int n = (int)Math.Min(16384, end - p + 1);
                    await ctx.Response.Body.WriteAsync(payload.AsMemory((int)p, n), ctx.RequestAborted);
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
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        self = new TestServer(app, payload, address);
        return self;
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int cur;
        while (value > (cur = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, cur) != cur) { }
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

