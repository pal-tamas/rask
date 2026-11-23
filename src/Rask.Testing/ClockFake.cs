namespace Rask.Testing;

/// <summary>A frozen clock a test moves by hand. Dispose it — <c>using var</c> — to put real time back.</summary>
/// <remarks>
///     It is a <see cref="TimeProvider" /> itself, and the one <c>Clock.Now</c> reads while it is open: hand it to a type
///     that takes one — <c>new PumpLanes(clock)</c> — and that type and the app's clock tell the same time, which
///     <see cref="Advance" /> moves for both.
/// </remarks>
public sealed class ClockFake : TimeProvider, IDisposable
{
    private readonly Lock _gate = new();
    private readonly IDisposable _scope;
    private DateTimeOffset _now;

    internal ClockFake(DateTimeOffset at)
    {
        _now = at;
        _scope = AmbientClock.Use(this);
    }

    /// <summary>The frozen time.</summary>
    public DateTimeOffset Now => GetUtcNow();

    /// <summary>The frozen time, as every reader of this clock is told it.</summary>
    /// <returns>The time the clock was frozen at, moved on by every <see cref="Advance" /> since.</returns>
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    /// <summary>Moves the clock on: <c>clock.Advance(2.Hours)</c>.</summary>
    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(by), by, "A clock only moves forward — Advance takes a positive duration.");
        }

        lock (_gate)
        {
            _now += by;
        }
    }

    /// <summary>Puts real time back.</summary>
    public void Dispose() => _scope.Dispose();
}
