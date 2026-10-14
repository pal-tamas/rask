using Microsoft.Playwright;

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>
///     The page's own clock — its timers, its animation frames, its <c>Date</c> — put in the test's hands, for a
///     journey whose subject is a time the page counts: a debounce, a toast's five seconds.
/// </summary>
/// <remarks>
///     A sleep measures the machine. With the clock stopped the browser can be driven as slowly as a loaded
///     runner drives it and no time passes on the page; <c>page.Clock.RunForAsync</c> then gives the page exactly
///     the milliseconds the journey is about, firing every timer and frame that falls in them.
/// </remarks>
internal static class PageClock
{
    private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Takes over the page's clock before it loads anything; time still runs until <see cref="StopAsync" />.</summary>
    public static Task Install(IPage page) => page.Clock.InstallAsync(new() { TimeDate = Start });

    /// <summary>Stops the page's time, an hour past where it started: later than any run reaches on its own.</summary>
    public static Task StopAsync(IPage page) => page.Clock.PauseAtAsync(Start.AddHours(1));

    /// <summary>
    ///     Stops the page's time for the <paramref name="nth" /> time in one context: that many hours past where
    ///     it started, since a clock set going again in between is stopped later than it has reached.
    /// </summary>
    public static Task StopAsync(IPage page, int nth) => page.Clock.PauseAtAsync(Start.AddHours(nth));
}
