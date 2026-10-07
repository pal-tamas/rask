using System.Globalization;

namespace Rask;

/// <summary>
///     The numbers a slider runs on: its ends, its step, and how close two thumbs may come.
/// </summary>
/// <remarks>
///     In <see cref="decimal" />, whatever the bound type: a step of <c>0.1</c> has to land on <c>0.3</c>, not
///     on <c>0.30000000000000004</c>.
/// </remarks>
internal sealed record UiSliderScale(decimal Min, decimal Max, decimal Step, decimal Gap)
{
    private decimal Span => Max - Min;

    internal static UiSliderScale Of(double? min, double? max, double? step, int? minStepsBetween)
    {
        var low = (decimal)(min ?? 0);
        var size = (decimal)(step ?? 1);
        size = size > 0 ? size : 1;

        return new UiSliderScale(low, Math.Max((decimal)(max ?? 100), low), size, Math.Max(minStepsBetween ?? 0, 0) * size);
    }

    /// <summary>How far along the track a value sits, 0 to 1.</summary>
    internal decimal Fraction(decimal value) =>
        Span == 0 ? 0 : Math.Clamp((value - Min) / Span, 0, 1);

    /// <summary>The nearest step to a value, held between the ends.</summary>
    internal decimal Snap(decimal value) =>
        Math.Clamp(Min + (Math.Round((value - Min) / Step, MidpointRounding.AwayFromZero) * Step), Min, Max);

    /// <summary>What a slider draws for a value it was not given: one thumb in the middle, two at the ends.</summary>
    internal decimal[] Normalize(decimal[] values, bool range)
    {
        if (!range)
        {
            return [Math.Clamp(values.Length > 0 ? values[0] : Min, Min, Max)];
        }

        var start = Math.Clamp(values.Length > 0 ? values[0] : Min, Min, Max);
        var end = Math.Clamp(values.Length > 1 ? values[1] : Max, start, Max);

        return [start, end];
    }

    /// <summary>The values after one thumb moved: on a step, and no nearer its neighbour than the gap.</summary>
    internal decimal[] Move(decimal[] values, int thumb, decimal to)
    {
        var moved = (decimal[])values.Clone();
        var (low, high) = Reach(values, thumb);
        moved[thumb] = Math.Clamp(Snap(to), low, Math.Max(low, high));

        return moved;
    }

    /// <summary>The lowest and highest value one thumb can take while its neighbour stays where it is.</summary>
    internal (decimal Low, decimal High) Reach(decimal[] values, int thumb) => (values.Length, thumb) switch
    {
        (2, 0) => (Min, Math.Max(Min, values[1] - Gap)),
        (2, 1) => (Math.Min(Max, values[0] + Gap), Max),
        _ => (Min, Max),
    };

    /// <summary>The thumb a click at a value moves: the nearer one.</summary>
    internal static int Nearest(decimal[] values, decimal to) =>
        values.Length == 2 && Math.Abs(values[1] - to) < Math.Abs(values[0] - to) ? 1 : 0;

    /// <summary>A number as HTML and CSS write it: a point, never a comma.</summary>
    internal static string Text(decimal value) =>
        value.ToString("0.############", CultureInfo.InvariantCulture);
}
