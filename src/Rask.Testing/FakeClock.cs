namespace Rask.Testing;

/// <summary><c>Clock.Fake(at: …)</c> — declared here so it exists only where Rask.Testing is referenced.</summary>
public static class ClockFakes
{
    extension(Clock)
    {
        /// <summary>
        ///     Freezes the app's clock at <paramref name="at" /> for this test until the returned clock is disposed:
        ///     <c>using var clock = Clock.Fake(at: monday9am);</c>. <c>Clock.Now</c>, <c>3.Days.Ago</c>, cache expiry and
        ///     audit stamps all read it; move it on with <c>clock.Advance(2.Hours)</c>.
        /// </summary>
        /// <remarks>Scoped to the test's own flow, so tests running in parallel never see each other's time.</remarks>
        public static FakeClock Fake(DateTimeOffset at) => new(at);
    }
}

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
