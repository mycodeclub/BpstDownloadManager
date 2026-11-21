namespace Bdm.Core;

/// <summary>
/// Token-bucket style speed limiter. Callers report bytes as they arrive; when they run ahead of the
/// allowed rate they are delayed. Rate 0 = unlimited. Thread-safe; can be changed while downloads run.
/// </summary>
public sealed class RateLimiter
{
    private readonly object _lock = new();
    private long _bytesPerSecond;
    private double _balance;        // bytes allowed right now (negative = debt to wait off)
    private long _lastTicks = Environment.TickCount64;

    public long BytesPerSecond
    {
        get => Interlocked.Read(ref _bytesPerSecond);
        set
        {
            lock (_lock)
            {
                Interlocked.Exchange(ref _bytesPerSecond, Math.Max(0, value));
                _balance = 0;
                _lastTicks = Environment.TickCount64;
            }
        }
    }

    public bool IsLimited => BytesPerSecond > 0;

    /// <summary>Accounts for <paramref name="bytes"/> just transferred and waits if over the limit.</summary>
    public async ValueTask ConsumeAsync(int bytes, CancellationToken ct)
    {
        long rate = BytesPerSecond;
        if (rate <= 0 || bytes <= 0) return;

        TimeSpan wait;
        lock (_lock)
        {
            long now = Environment.TickCount64;
            double elapsed = (now - _lastTicks) / 1000.0;
            _lastTicks = now;
            // Allow at most a 1 second burst.
            _balance = Math.Min(rate, _balance + elapsed * rate);
            _balance -= bytes;
            wait = _balance >= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(-_balance / rate);
        }
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait > TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : wait, ct);
    }
}
