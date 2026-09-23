namespace Rask;

/// <summary>The value axis of a chart: its ends, and the round steps between them.</summary>
/// <remarks>
/// "Nice numbers": the ends are widened to multiples of a step of 1, 2 or 5 times a power of ten, so the axis reads
/// 0, 250, 500, 750, 1,000 rather than 0, 237.5, 475. Zero is on the axis unless every value is on one side of it
/// and the caller moved it — a bar that starts at 80 instead of 0 is a bar that lies about its size.
/// </remarks>
internal readonly record struct UiChartScale(double Min, double Max, double Step)
{
    private const int Steps = 4;

    internal IEnumerable<double> Ticks
    {
        get
        {
            var count = (int)Math.Round((Max - Min) / Step);
            for (var i = 0; i <= count; i++)
            {
                yield return Min + (i * Step);
            }
        }
    }

    internal static UiChartScale For(IEnumerable<double> values, double? min, double? max)
    {
        var lo = min ?? 0;
        var hi = max ?? 0;
        var any = false;
        foreach (var v in values)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                continue;
            }

            any = true;
            if (min is null)
            {
                lo = Math.Min(lo, v);
            }

            if (max is null)
            {
                hi = Math.Max(hi, v);
            }
        }

        if (!any || hi <= lo)
        {
            hi = lo + 1;
        }

        var step = Nice((hi - lo) / Steps, round: true);
        var niceMin = min ?? Math.Floor(lo / step) * step;
        var niceMax = max ?? Math.Ceiling(hi / step) * step;
        return new UiChartScale(niceMin, niceMax, step);
    }

    private static double Nice(double range, bool round)
    {
        var exponent = Math.Floor(Math.Log10(range));
        var fraction = range / Math.Pow(10, exponent);
        var nice = round
            ? fraction < 1.5 ? 1 : fraction < 3 ? 2 : fraction < 7 ? 5 : 10
            : fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
        return nice * Math.Pow(10, exponent);
    }
}
