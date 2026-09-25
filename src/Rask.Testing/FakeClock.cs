namespace Rask.Testing;

/// <summary>A frozen clock a test moves by hand. Dispose it — <c>using var</c> — to put real time back.</summary>
public sealed class FakeClock : IDisposable
{
    private readonly Frozen _time;
    private readonly IDisposable _scope;

    internal FakeClock(DateTimeOffset at)
    {
        _time = new Frozen(at);
        _scope = AmbientClock.Use(_time);
    }

    /// <summary>The frozen time.</summary>
    public DateTimeOffset Now => _time.GetUtcNow();

    /// <summary>Moves the clock on: <c>clock.Advance(2.Hours)</c>.</summary>
    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(by), by, "A clock only moves forward — Advance takes a positive duration.");
        }

        _time.Now += by;
    }

    /// <summary>Puts real time back.</summary>
    public void Dispose() => _scope.Dispose();

    private sealed class Frozen(DateTimeOffset at) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = at;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
