using System.ComponentModel;

namespace Rask;

/// <summary>
///     The time, as the app sees it: <c>Clock.Now</c>. The same clock <c>3.Days.Ago</c>, the cache's expiry, an
///     entity's audit stamps and a job's schedule read — so a test that freezes it with <c>Clock.Fake(at: …)</c>
///     moves all of them together.
/// </summary>
public static class Clock
{
    /// <summary>Now, in UTC — the test's frozen time when one is in scope.</summary>
    public static DateTimeOffset Now => AmbientClock.Now;

    /// <summary>
    ///     This clock as a <see cref="System.TimeProvider" />, for code that takes one — which is what the batteries
    ///     register, so their timestamps follow <see cref="Now" />.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TimeProvider TimeProvider { get; } = new ClockTimeProvider();

    // Reads the ambient clock for the wall-clock time only. Elapsed-time measurement and timers stay on the
    // system clock, which is what they measure.
    private sealed class ClockTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => AmbientClock.Now;
    }
}
