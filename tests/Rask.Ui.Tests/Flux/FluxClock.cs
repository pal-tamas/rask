using Rask.Core;

namespace Rask.UiTests.Flux;

/// <summary>
///     The two clocks Flux's docs pages run on, for a parity page to run on the same ones.
/// </summary>
/// <remarks>
///     <c>scripts/flux/lib.mjs</c> pins the BROWSER to 15 January 2026, and Flux's calendar asks the browser what
///     today is. The dates its examples are given (<c>now()-&gt;format('Y-m-d')</c>) are written by Flux's SERVER,
///     which is on the real date. A translated example needs both to land on the month Flux's lands on.
/// </remarks>
internal static class FluxClock
{
    /// <summary>What the measuring browser says today is.</summary>
    internal static readonly DateOnly Today = new(2026, 1, 15);

    /// <summary>Flux's <c>now()</c>: the day the docs page was rendered.</summary>
    internal static DateOnly Now => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Flux's <c>now()-&gt;day(n)</c>.</summary>
    internal static DateOnly Day(int day) => new(Now.Year, Now.Month, day);

    /// <summary>Tells a control it is <paramref name="today" />, as the measuring browser tells Flux's.</summary>
    internal static T On<T>(this T control, DateOnly today)
        where T : Component, IUiClock
    {
        control.Today = today;
        return control;
    }
}
