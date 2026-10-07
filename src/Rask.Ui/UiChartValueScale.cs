namespace Rask;

/// <summary>The value axis of a chart: its ends, and the round ticks between them. Flux's rules, as measured.</summary>
/// <remarks>
/// <para>
/// The axis starts at zero unless a value is below it, and ends at the highest value. The step is the "nice"
/// number — 1, 2 or 5 times a power of ten — nearest a quarter of that span (one less than the tick count of
/// five). An end within a tenth of a step of the next tick is moved out to it, so 297 is drawn to 300 and 281
/// is not.
/// </para>
/// </remarks>
internal sealed class UiChartValueScale
{
    private UiChartValueScale(double low, double high, double[] ticks)
    {
        Low = low;
        High = high;
        Ticks = ticks;
    }

    internal double Low { get; }

    internal double High { get; }

    internal double[] Ticks { get; }

    /// <summary>Where <paramref name="value" /> sits between <see cref="Low" /> (0) and <see cref="High" /> (1).</summary>
    internal double Fraction(double value) => (value - Low) / (High - Low);

    internal static UiChartValueScale For(double min, double max, UiChartAxis? axis)
    {
        var low = axis?.TickStart ?? (min >= 0 ? 0 : min);
        var high = axis?.TickEnd ?? max;
        if (!(high > low))
        {
            high = low + 1;
        }

        var step = Nice((high - low) / Math.Max(1, (axis?.TickCount ?? 5) - 1));
        if (axis?.TickEnd is null && Math.Ceiling((high / step) - Epsilon) * step is var above && above - high <= (step * 0.1) + (step * Epsilon))
        {
            high = above;
        }

        if (axis?.TickStart is null && low < 0 && Math.Floor((low / step) + Epsilon) * step is var below && low - below <= (step * 0.1) + (step * Epsilon))
        {
            low = below;
        }

        return new UiChartValueScale(low, high, axis?.TickValues is { } given ? Within(given, low, high) : Steps(low, high, step));
    }

    private const double Epsilon = 1e-9;

    private static double[] Steps(double low, double high, double step)
    {
        var first = (long)Math.Ceiling((low / step) - Epsilon);
        var last = (long)Math.Floor((high / step) + Epsilon);
        // A step of 0.2 times three is 0.6000000000000001: each tick is rounded to the digits its step has.
        var digits = Math.Clamp(1 - (int)Math.Floor(Math.Log10(step)), 0, 15);
        var ticks = new double[Math.Max(0, last - first + 1)];
        for (var i = 0; i < ticks.Length; i++)
        {
            ticks[i] = Math.Round((first + i) * step, digits);
        }

        return ticks;
    }

    private static double[] Within(IReadOnlyList<double> given, double low, double high) =>
        [.. given.Where(tick => tick >= low && tick <= high)];

    private static double Nice(double rough)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        return (rough / magnitude) switch
        {
            < 1.5 => 1,
            < 3 => 2,
            < 7 => 5,
            _ => 10,
        } * magnitude;
    }
}
