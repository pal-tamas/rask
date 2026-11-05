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
    ///     Now, as the clock on the wall shows it — in <see cref="Zone" />, whatever the server's own zone is:
    ///     <c>Clock.Wall</c> is <c>14:35</c> in Budapest on a server that runs on UTC.
    /// </summary>
    /// <remarks>
    ///     Its <see cref="DateTime.Kind" /> is <see cref="DateTimeKind.Unspecified" />: a wall-clock time, which is how a
    ///     table that keeps moments "as the clock showed them" reads them back. It follows <see cref="Now" />, so a
    ///     frozen test clock freezes it too, across a change to or from summer time.
    /// </remarks>
    public static DateTime Wall => AmbientClock.OnTheWall(AmbientClock.Now);

    /// <summary>Today, as the calendar on the wall shows it — the day of <see cref="Wall" />.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(Wall);

    /// <summary>
    ///     The zone the wall clock is in: the one the app named — <c>AddRask(timeZone: "Europe/Budapest")</c>, or
    ///     <c>c.TimeZone</c> on a <c>RaskApp</c> — and otherwise the machine's (in a browser app, the browser's).
    /// </summary>
    public static TimeZoneInfo Zone => AmbientClock.Zone;

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

        public override TimeZoneInfo LocalTimeZone => AmbientClock.Zone;
    }
}
