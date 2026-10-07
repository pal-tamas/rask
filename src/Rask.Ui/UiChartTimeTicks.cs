namespace Rask;

/// <summary>The ticks of a time axis, and how they are written when no format says. Flux's rules, as measured.</summary>
/// <remarks>
/// <para>
/// The unit follows the span — minutes under an hour, hours to a week, days to 180 days, months to two years,
/// then years — and the step is as many whole units as lie between two rows, so an axis ticks at the pace its
/// data arrives. The first tick is the first row itself; the rest fall on whole units.
/// </para>
/// <para>
/// The label follows the distance from the first tick to the last: <c>9:17 AM</c>, <c>9 AM</c>, <c>Tue 9 AM</c>,
/// <c>Mar 10</c>, <c>Mar</c>, <c>2026</c>.
/// </para>
/// </remarks>
internal static class UiChartTimeTicks
{
    private const int Limit = 500;

    internal static List<DateTime> For(DateTime first, DateTime last, int rows)
    {
        var span = last - first;
        var pace = rows > 1 ? span / (rows - 1) : TimeSpan.Zero;
        var ticks = new List<DateTime> { first };
        Func<DateTime, int, DateTime> step;
        DateTime start;
        if (span < TimeSpan.FromHours(1))
        {
            (start, step) = Fixed(first, pace, TimeSpan.FromMinutes(1));
        }
        else if (span <= TimeSpan.FromDays(7))
        {
            (start, step) = Fixed(first, pace, TimeSpan.FromHours(1));
        }
        else if (span <= TimeSpan.FromDays(180))
        {
            (start, step) = Fixed(first, pace, TimeSpan.FromDays(1));
        }
        else if (span < TimeSpan.FromDays(730))
        {
            var months = Whole(pace, TimeSpan.FromDays(30));
            start = new DateTime(first.Year, first.Month, 1, 0, 0, 0, first.Kind);
            step = (from, index) => from.AddMonths(index * months);
        }
        else
        {
            var years = Whole(pace, TimeSpan.FromDays(365));
            start = new DateTime(first.Year, 1, 1, 0, 0, 0, first.Kind);
            step = (from, index) => from.AddYears(index * years);
        }

        for (var index = 1; index < Limit; index++)
        {
            var tick = step(start, index);
            if (tick > last)
            {
                break;
            }

            if (tick > first)
            {
                ticks.Add(tick);
            }
        }

        return ticks;
    }

    /// <summary>The .NET pattern for ticks that reach from <paramref name="first" /> to <paramref name="last" />.</summary>
    internal static string Pattern(DateTime first, DateTime last) => (last - first) switch
    {
        var reach when reach < TimeSpan.FromHours(1) => "h:mm tt",
        var reach when reach < TimeSpan.FromDays(1) => "h tt",
        var reach when reach < TimeSpan.FromDays(7) => "ddd h tt",
        var reach when reach < TimeSpan.FromDays(180) => "MMM d",
        var reach when reach < TimeSpan.FromDays(730) => "MMM",
        _ => "yyyy",
    };

    private static (DateTime Start, Func<DateTime, int, DateTime> Step) Fixed(DateTime first, TimeSpan pace, TimeSpan unit)
    {
        var every = unit * Whole(pace, unit);
        return (new DateTime(first.Ticks - (first.Ticks % unit.Ticks), first.Kind), (from, index) => from + (every * index));
    }

    private static int Whole(TimeSpan pace, TimeSpan unit) => (int)Math.Max(1, pace.Ticks / unit.Ticks);
}
