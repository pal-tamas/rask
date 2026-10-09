using System.Globalization;
using System.Text;

namespace Rask;

/// <summary>The path data a chart draws: Flux's curves, bars and slices, command for command.</summary>
internal static class UiChartPaths
{
    /// <summary>A coordinate as the shortest text that reads back as the same number, as JavaScript writes one.</summary>
    internal static StringBuilder Number(this StringBuilder path, double value) =>
        path.Append((value == 0 ? 0 : value).ToString("R", CultureInfo.InvariantCulture));

    private static StringBuilder Point(this StringBuilder path, double x, double y, char separator = ',') =>
        path.Number(x).Append(separator).Number(y);

    /// <summary>
    /// Flux's <c>smooth</c>: a monotone cubic through every point (Fritsch–Carlson tangents, as Steffen limits
    /// them), which never overshoots a value. Fitted to the measured paths: the control points sit a third of the
    /// way along each segment.
    /// </summary>
    internal static void Smooth(StringBuilder path, ReadOnlySpan<double> x, ReadOnlySpan<double> y)
    {
        if (x.Length == 0)
        {
            return;
        }

        path.Append('M').Point(x[0], y[0]);
        if (x.Length == 2)
        {
            path.Append('L').Point(x[1], y[1]);
        }

        if (x.Length < 3)
        {
            return;
        }

        var tangent = 0d;
        for (var i = 2; i < x.Length; i++)
        {
            var next = Slope(x[i - 2], y[i - 2], x[i - 1], y[i - 1], x[i], y[i]);
            Curve(path, x[i - 2], y[i - 2], x[i - 1], y[i - 1], i == 2 ? EndSlope(x[0], y[0], x[1], y[1], next) : tangent, next);
            tangent = next;
        }

        var last = x.Length - 1;
        Curve(path, x[last - 1], y[last - 1], x[last], y[last], tangent, EndSlope(x[last - 1], y[last - 1], x[last], y[last], tangent));
    }

    /// <summary>Flux's <c>none</c>: straight segments, written as curves whose handles lie on their own ends.</summary>
    internal static void Straight(StringBuilder path, ReadOnlySpan<double> x, ReadOnlySpan<double> y)
    {
        for (var i = 0; i < x.Length; i++)
        {
            if (i == 0)
            {
                path.Append("M ").Point(x[0], y[0], ' ');
                continue;
            }

            path.Append(" C ").Point(x[i - 1], y[i - 1], ' ').Append(", ").Point(x[i], y[i], ' ').Append(", ").Point(x[i], y[i], ' ');
        }
    }

    /// <summary>Closes a line down to the baseline it stands on, making it the area under it.</summary>
    internal static void Close(StringBuilder path, double firstX, double lastX, double baseline) =>
        path.Append(" L").Point(lastX, baseline).Append(" L").Point(firstX, baseline).Append(" Z");

    /// <summary>
    /// An upright bar from <paramref name="baseline" /> to <paramref name="value" />, its value end rounded by
    /// <paramref name="radius" />. A bar below the baseline is rounded at the bottom.
    /// </summary>
    internal static void Bar(StringBuilder path, double x, double width, double value, double baseline, double radius)
    {
        var top = Math.Min(value, baseline);
        var height = Math.Abs(baseline - value);
        var r = Corner(radius, width, height);
        if (r <= 0)
        {
            path.Append('M').Point(x, top).Append(" h").Number(width).Append(" v").Number(height)
                .Append(" h-").Number(width).Append(" v-").Number(height);
        }
        else if (value <= baseline)
        {
            path.Append('M').Point(x, top + r).Arc(r, x + r, top).Append('H').Number(x + width - r).Arc(r, x + width, top + r)
                .Append('V').Number(baseline).Append('H').Number(x).Append('V').Number(top + r).Append('Z');
        }
        else
        {
            path.Append('M').Point(x, baseline).Append('H').Number(x + width).Append('V').Number(value - r).Arc(r, x + width - r, value)
                .Append('H').Number(x + r).Arc(r, x, value - r).Append('V').Number(baseline).Append('Z');
        }
    }

    /// <summary>
    /// A bar lying on its side, from <paramref name="baseline" /> out to <paramref name="value" />. One that runs
    /// back from the baseline is rounded at its left end.
    /// </summary>
    internal static void HorizontalBar(StringBuilder path, double y, double height, double value, double baseline, double radius)
    {
        var width = Math.Abs(value - baseline);
        var r = Corner(radius, height, width);
        if (r <= 0)
        {
            path.Append('M').Point(Math.Min(value, baseline), y).Append(" h").Number(width).Append(" v").Number(height)
                .Append(" h-").Number(width).Append(" v-").Number(height);
        }
        else if (value >= baseline)
        {
            path.Append('M').Point(baseline, y).Append('H').Number(value - r).Arc(r, value, y + r).Append('V').Number(y + height - r)
                .Arc(r, value - r, y + height).Append('H').Number(baseline).Append('V').Number(y).Append('Z');
        }
        else
        {
            path.Append('M').Point(value, y + r).Arc(r, value + r, y).Append('H').Number(baseline).Append('V').Number(y + height)
                .Append('H').Number(value + r).Arc(r, value, y + height - r).Append('V').Number(y + r).Append('Z');
        }
    }

    // Two corners share the value end, so each takes at most half of it — and at most half the bar's own
    // length, which is what keeps a bar shorter than its corners a half disc and not a shape that folds back.
    private static double Corner(double radius, double thickness, double length) =>
        Math.Min(radius, Math.Min(thickness / 2, length / 2));

    /// <summary>
    /// One slice of a pie or a donut, clockwise from <paramref name="from" /> to <paramref name="to" /> (radians
    /// from twelve o'clock). With a corner radius, each corner is the circle that touches both the slice's edge
    /// and its arc.
    /// </summary>
    internal static void Sector(StringBuilder path, double cx, double cy, double outer, double inner, double corner, double from, double to)
    {
        if (inner <= 0 && corner <= 0 && to - from >= Math.Tau - 1e-9)
        {
            // The only slice: a whole disc, as two half turns — one arc cannot end where it began.
            path.Append('M').At(cx, cy, outer, 0).Append(" A").Point(outer, outer).Append(" 0 1 1 ").Point(cx, cy + outer)
                .Append(" A").Point(outer, outer).Append(" 0 1 1 ").Point(cx, cy - outer).Append(" Z");
            return;
        }

        if (inner <= 0 && corner <= 0)
        {
            path.Append('M').At(cx, cy, outer, from).Append(" A").Point(outer, outer).Append(" 0 ").Append(to - from > Math.PI ? '1' : '0')
                .Append(" 1 ").At(cx, cy, outer, to).Append(" L").Point(cx, cy).Append(" Z");
            return;
        }

        if (corner <= 0)
        {
            var large = to - from > Math.PI ? '1' : '0';
            path.Append('M').At(cx, cy, outer, from).Append(" A").Point(outer, outer).Append(" 0 ").Append(large).Append(" 1 ").At(cx, cy, outer, to)
                .Append(" L").At(cx, cy, inner, to).Append(" A").Point(inner, inner).Append(" 0 ").Append(large).Append(" 0 ").At(cx, cy, inner, from).Append(" Z");
            return;
        }

        // Where a corner circle touches the straight edge, and how far round the arc it takes its other touch.
        var outerEdge = Math.Sqrt(((outer - corner) * (outer - corner)) - (corner * corner));
        var outerTurn = Math.Asin(corner / (outer - corner));
        var innerEdge = Math.Sqrt(((inner + corner) * (inner + corner)) - (corner * corner));
        var innerTurn = Math.Asin(corner / (inner + corner));
        path.Append('M').At(cx, cy, outerEdge, from)
            .Round(corner).At(cx, cy, outer, from + outerTurn)
            .Append(" A").Point(outer, outer).Append(" 0 ").Append(to - from - (2 * outerTurn) > Math.PI ? '1' : '0').Append(" 1 ").At(cx, cy, outer, to - outerTurn)
            .Round(corner).At(cx, cy, outerEdge, to)
            .Append(" L").At(cx, cy, innerEdge, to)
            .Round(corner).At(cx, cy, inner, to - innerTurn)
            .Append(" A").Point(inner, inner).Append(" 0 ").Append(to - from - (2 * innerTurn) > Math.PI ? '1' : '0').Append(" 0 ").At(cx, cy, inner, from + innerTurn)
            .Round(corner).At(cx, cy, innerEdge, from)
            .Append(" Z");
    }

    private static StringBuilder At(this StringBuilder path, double cx, double cy, double radius, double angle) =>
        path.Point(cx + (radius * Math.Sin(angle)), cy - (radius * Math.Cos(angle)));

    private static StringBuilder Round(this StringBuilder path, double corner) =>
        path.Append(" A").Point(corner, corner).Append(" 0 0 1 ");

    private static StringBuilder Arc(this StringBuilder path, double radius, double x, double y) =>
        path.Append('A').Point(radius, radius).Append(" 0 0 1 ").Point(x, y);

    private static void Curve(StringBuilder path, double x0, double y0, double x1, double y1, double t0, double t1)
    {
        var third = (x1 - x0) / 3;
        path.Append('C').Point(x0 + third, y0 + (third * t0)).Append(' ').Point(x1 - third, y1 - (third * t1)).Append(' ').Point(x1, y1);
    }

    // The tangent at the middle of three points: zero where the slope changes sign, and never steeper than
    // twice the gentler of the two segments it joins.
    private static double Slope(double x0, double y0, double x1, double y1, double x2, double y2)
    {
        var h0 = x1 - x0;
        var h1 = x2 - x1;
        var s0 = (y1 - y0) / h0;
        var s1 = (y2 - y1) / h1;
        var p = ((s0 * h1) + (s1 * h0)) / (h0 + h1);
        var slope = (Sign(s0) + Sign(s1)) * Math.Min(Math.Min(Math.Abs(s0), Math.Abs(s1)), 0.5 * Math.Abs(p));
        return double.IsNaN(slope) ? 0 : slope;
    }

    private static double EndSlope(double x0, double y0, double x1, double y1, double tangent)
    {
        var h = x1 - x0;
        return h == 0 ? tangent : ((3 * (y1 - y0) / h) - tangent) / 2;
    }

    private static int Sign(double value) => value < 0 ? -1 : 1;
}
