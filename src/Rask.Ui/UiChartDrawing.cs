using System.Buffers;
using System.Globalization;
using System.Net;
using System.Text;

namespace Rask;

/// <summary>Writes a laid-out chart as the SVG Flux ends up with, node for node and in Flux's order.</summary>
/// <remarks>
/// Tick labels, then grid lines, then axis lines, then the series as declared, then the cursor and the overlay
/// that takes the pointer. Written straight into one string: a chart is hundreds of nodes, redrawn whenever its
/// data moves, and an element object per node would be most of what a render allocates.
/// </remarks>
internal sealed class UiChartDrawing(UiChartPlot plot, UiChartParts parts, UiChartData data)
{
    private const string BottomLabel = "block text-xs font-medium text-zinc-400 dark:text-zinc-300";
    private const string SideLabel = "block text-xs text-zinc-400 dark:text-zinc-300";
    private const string GridLine = "text-zinc-200/50 dark:text-white/15";
    private const string AxisLine = "text-zinc-300 dark:text-white/40";
    private const string CursorLine = "text-zinc-500 dark:text-zinc-300";
    private const string PointRing = "stroke-white dark:stroke-zinc-900";

    private StringBuilder _svg = null!;

    /// <summary>Writes the chart's nodes into <paramref name="svg" />, between the tags its caller writes.</summary>
    internal void Write(StringBuilder svg)
    {
        _svg = svg;
        var x = parts.X;
        var y = parts.Y;
        if (x?.Tick is { } bottom)
        {
            BottomLabels(bottom);
        }

        if (y?.Tick is { } side)
        {
            SideLabels(y, side);
        }

        Grid(x, vertical: true);
        Grid(y, vertical: false);
        if (x?.Line is { } floor)
        {
            Line(plot.Left, plot.Right, plot.Bottom, plot.Bottom, AxisLine, floor.Class, floor.StrokeWidth, null, "fill=\"none\" ");
        }

        if (y?.Line is { } wall)
        {
            var at = y.Position == Ui.Position.Right ? plot.Right : plot.Left;
            Line(at, at, plot.Top, plot.Bottom, AxisLine, wall.Class, wall.StrokeWidth, null, "fill=\"none\" ");
        }

        Marks(x, vertical: true);
        Marks(y, vertical: false);
        ZeroLine();
        Cursor(Ui.ChartCursorType.Area);
        foreach (var part in parts.Series)
        {
            Series(part);
        }

        Cursor(Ui.ChartCursorType.Line);
    }

    // The x axis's labels hang under the plot: the rows' names on an upright chart, the values on one lying down.
    private void BottomLabels(UiChartAxisTick tick)
    {
        _svg.Append("<g>");
        var labels = plot.Horizontal ? plot.ValueLabels : System.Runtime.InteropServices.CollectionsMarshal.AsSpan(plot.IndexLabels);
        for (var i = 0; i < labels.Length; i++)
        {
            var at = plot.Horizontal ? plot.Left + (labels[i].Unit * (plot.Right - plot.Left)) : plot.Index(labels[i].Unit);
            var hidden = !plot.Horizontal && plot.Hidden(i, labels.Length);
            Translate(at, plot.Bottom, Shown(hidden));
            _svg.Append("<text text-anchor=\"middle\" fill=\"currentColor\" dominant-baseline=\"text-before-edge\" dy=\"1em\"");
            Class(BottomLabel, tick.Class);
            if (!plot.Horizontal && !hidden)
            {
                _svg.Append(plot.Crowding switch
                {
                    UiChartCrowding.Turned => " style=\"transform: rotate(-45deg); text-anchor: end;\"",
                    UiChartCrowding.Ends => i == 0 ? " style=\"text-anchor: start;\"" : " style=\"text-anchor: end;\"",
                    _ => "",
                });
            }

            _svg.Append('>').Append(WebUtility.HtmlEncode(labels[i].Text)).Append("</text></g>");
        }

        _svg.Append("</g>");
    }

    // Flux hides a label that gave way with an inline display, and states it on the two it keeps at the ends.
    private string Shown(bool hidden)
    {
        if (hidden)
        {
            return " style=\"display: none;\"";
        }

        return plot.Crowding == UiChartCrowding.Ends && !plot.Horizontal ? " style=\"display: inline;\"" : "";
    }

    private void SideLabels(UiChartAxis axis, UiChartAxisTick tick)
    {
        var right = axis.Position == Ui.Position.Right;
        _svg.Append("<g>");
        var labels = plot.Horizontal ? System.Runtime.InteropServices.CollectionsMarshal.AsSpan(plot.IndexLabels) : plot.ValueLabels;
        foreach (var label in labels)
        {
            var at = plot.Horizontal ? plot.Index(label.Unit) : plot.Bottom - (label.Unit * (plot.Bottom - plot.Top));
            Translate(right ? plot.Right : plot.Left, at, "");
            _svg.Append(right
                ? "<text dominant-baseline=\"central\" fill=\"currentColor\" text-anchor=\"start\" dx=\"1em\""
                : "<text dominant-baseline=\"central\" fill=\"currentColor\" text-anchor=\"end\" dx=\"-1em\"");
            Class(SideLabel, tick.Class);
            _svg.Append('>').Append(WebUtility.HtmlEncode(label.Text)).Append("</text></g>");
        }

        _svg.Append("</g>");
    }

    private void Translate(double x, double y, string style) =>
        _svg.Append("<g transform=\"translate(").Number(x).Append(", ").Number(y).Append(")\"").Append(style).Append('>');

    // A line across the plot at every tick of the axis: upright ones for the x axis, level ones for the y axis.
    private void Grid(UiChartAxis? axis, bool vertical)
    {
        if (axis?.Grid is not { } grid)
        {
            return;
        }

        _svg.Append("<g>");
        foreach (var at in Ticks(vertical))
        {
            if (vertical)
            {
                Line(at, at, plot.Top, plot.Bottom, GridLine, grid.Class, grid.StrokeWidth, grid.StrokeDasharray);
            }
            else
            {
                Line(plot.Left, plot.Right, at, at, GridLine, grid.Class, grid.StrokeWidth, grid.StrokeDasharray);
            }
        }

        _svg.Append("</g>");
    }

    private void Marks(UiChartAxis? axis, bool vertical)
    {
        if (axis?.Mark is not { } mark)
        {
            return;
        }

        const double Length = 6;
        _svg.Append("<g>");
        foreach (var at in Ticks(vertical))
        {
            if (vertical)
            {
                var from = mark.Position == Ui.Position.Top ? plot.Top : plot.Bottom;
                Line(at, at, from, mark.Position == Ui.Position.Top ? from - Length : from + Length, AxisLine, mark.Class, mark.StrokeWidth, null);
            }
            else
            {
                var right = mark.Position == Ui.Position.Right || axis.Position == Ui.Position.Right;
                var from = right ? plot.Right : plot.Left;
                Line(from, right ? from + Length : from - Length, at, at, AxisLine, mark.Class, mark.StrokeWidth, null);
            }
        }

        _svg.Append("</g>");
    }

    // Where the ticks of the x axis (vertical lines) or the y axis (level lines) fall, in pixels.
    private List<double> Ticks(bool vertical)
    {
        var ticks = new List<double>();
        if (vertical != plot.Horizontal)
        {
            foreach (var label in plot.IndexLabels)
            {
                ticks.Add(plot.Index(label.Unit));
            }
        }
        else
        {
            foreach (var tick in plot.Values.Ticks)
            {
                ticks.Add(plot.Value(tick));
            }
        }

        return ticks;
    }

    private void ZeroLine()
    {
        if (parts.ZeroLine is not { } zero || plot.Values.Low >= 0)
        {
            return;
        }

        var at = plot.Value(0);
        if (plot.Horizontal)
        {
            Line(at, at, plot.Top, plot.Bottom, AxisLine, zero.Class, zero.StrokeWidth, null);
        }
        else
        {
            Line(plot.Left, plot.Right, at, at, AxisLine, zero.Class, zero.StrokeWidth, null);
        }
    }

    private void Line(double x1, double x2, double y1, double y2, string look, string? own, double? width, string? dashes, string extra = "")
    {
        _svg.Append("<line stroke=\"currentColor\" ").Append(extra).Append("stroke-width=\"").Number(width ?? 1).Append('"');
        Dashes(dashes);
        Class(look, own);
        _svg.Append(" x1=\"").Number(x1).Append("\" x2=\"").Number(x2).Append("\" y1=\"").Number(y1).Append("\" y2=\"").Number(y2).Append("\"></line>");
    }

    // Resting, the cursor is in the document and invisible, as Flux leaves it until the pointer arrives.
    private void Cursor(Ui.ChartCursorType type)
    {
        if (parts.Cursor is not { } cursor || (cursor.Type ?? Ui.ChartCursorType.Line) != type)
        {
            return;
        }

        _svg.Append("<path fill=\"none\" stroke=\"currentColor\" stroke-width=\"").Number(cursor.StrokeWidth ?? 1)
            .Append("\" stroke-dasharray=\"").Append(WebUtility.HtmlEncode(cursor.StrokeDasharray ?? "4,4")).Append('"');
        Class(CursorLine, cursor.Class);
        _svg.Append(" opacity=\"0\"></path>");
    }

    private void Series(Component part)
    {
        switch (part)
        {
            case UiChartLine line:
                _svg.Append("<path stroke=\"currentColor\" stroke-width=\"").Append('2')
                    .Append("\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\"");
                Dashes(line.StrokeDasharray);
                Class(null, line.Class);
                Curve(line.Field, line.Curve, close: false);
                break;
            case UiChartArea area:
                _svg.Append("<path fill=\"currentColor\"");
                Class(null, area.Class);
                Curve(area.Field, area.Curve, close: true);
                break;
            case UiChartPoint point:
                Points(point);
                break;
            case UiChartBar bar:
                _svg.Append("<g>");
                Bars(bar, slot: 0, slots: 1, share: Share(bar.Width, 0.9), radius: Radius(bar.Radius, 8), stack: null);
                _svg.Append("</g>");
                break;
            case UiChartGroup group:
                Group(group);
                break;
            case UiChartStack stack:
                Stack(stack);
                break;
            default:
                break;
        }
    }

    private void Curve(UiChartField? field, Ui.ChartCurve? curve, bool close)
    {
        var count = data.Count;
        var x = ArrayPool<double>.Shared.Rent(count);
        var y = ArrayPool<double>.Shared.Rent(count);
        var drawn = 0;
        for (var row = 0; row < count; row++)
        {
            var value = parts.Read(field, row);
            if (double.IsNaN(value))
            {
                continue;
            }

            (x[drawn], y[drawn]) = plot.Horizontal ? (plot.Value(value), plot.Index(plot.Units[row])) : (plot.Index(plot.Units[row]), plot.Value(value));
            drawn++;
        }

        _svg.Append(" d=\"");
        // A chart lying down runs its line down the page; the smooth curve is defined along x, so it is drawn straight.
        if (curve == Ui.ChartCurve.None || plot.Horizontal)
        {
            UiChartPaths.Straight(_svg, x.AsSpan(0, drawn), y.AsSpan(0, drawn));
        }
        else
        {
            UiChartPaths.Smooth(_svg, x.AsSpan(0, drawn), y.AsSpan(0, drawn));
        }

        if (close && drawn > 0 && !plot.Horizontal)
        {
            UiChartPaths.Close(_svg, x[0], x[drawn - 1], plot.Baseline);
        }

        _svg.Append("\"></path>");
        ArrayPool<double>.Shared.Return(x);
        ArrayPool<double>.Shared.Return(y);
    }

    private void Points(UiChartPoint point)
    {
        _svg.Append("<g>");
        for (var row = 0; row < data.Count; row++)
        {
            var value = parts.Read(point.Field, row);
            if (double.IsNaN(value))
            {
                continue;
            }

            var (x, y) = plot.Horizontal ? (plot.Value(value), plot.Index(plot.Units[row])) : (plot.Index(plot.Units[row]), plot.Value(value));
            _svg.Append("<circle r=\"").Number(point.R ?? 4).Append("\" fill=\"currentColor\" stroke-width=\"").Number(point.StrokeWidth ?? 1).Append('"');
            Class(PointRing, point.Class);
            _svg.Append(" cx=\"").Number(x).Append("\" cy=\"").Number(y).Append("\" data-rask-plot-row=\"").Append(row).Append("\"></circle>");
        }

        _svg.Append("</g>");
    }

    // Side by side: the group fills nine tenths of the row's band, and a fiftieth of that parts each bar from the next.
    private void Group(UiChartGroup group)
    {
        var bars = group.Bars();
        _svg.Append("<g>");
        for (var slot = 0; slot < bars.Count; slot++)
        {
            _svg.Append("<g>");
            Bars(bars[slot], slot, bars.Count, Share(group.Width, 0.9), Radius(bars[slot].Radius, 4), stack: null);
            _svg.Append("</g>");
        }

        _svg.Append("</g>");
    }

    private void Stack(UiChartStack stack)
    {
        var bars = stack.Bars();
        var floor = new double[data.Count];
        _svg.Append("<g>");
        foreach (var bar in bars)
        {
            _svg.Append("<g>");
            Bars(bar, slot: 0, slots: 1, Share(stack.Width, 0.9), Radius(bar.Radius, 0), floor);
            _svg.Append("</g>");
        }

        _svg.Append("</g>");
    }

    private void Bars(UiChartBar bar, int slot, int slots, (double Fraction, double Pixels) share, double radius, double[]? stack)
    {
        var band = plot.Band * (plot.Horizontal ? plot.Bottom - plot.Top : plot.Right - plot.Left);
        var across = share.Pixels > 0 ? share.Pixels : band * share.Fraction;
        var gap = slots > 1 ? across * 0.02 : 0;
        var thickness = (across - (gap * (slots - 1))) / slots;
        for (var row = 0; row < data.Count; row++)
        {
            var value = parts.Read(bar.Field, row);
            value = double.IsNaN(value) ? 0 : value;
            var from = stack is null ? Math.Clamp(0, plot.Values.Low, plot.Values.High) : stack[row];
            var to = from + value;
            if (stack is not null)
            {
                stack[row] = to;
            }

            var start = plot.Index(plot.Units[row]) - (across / 2) + (slot * (thickness + gap));
            _svg.Append("<path stroke=\"none\" fill=\"currentColor\"");
            Class(null, bar.Class);
            _svg.Append(" d=\"");
            if (plot.Horizontal)
            {
                UiChartPaths.HorizontalBar(_svg, start, thickness, plot.Value(to), plot.Value(from), radius);
            }
            else
            {
                UiChartPaths.Bar(_svg, start, thickness, plot.Value(to), plot.Value(from), radius);
            }

            _svg.Append("\"></path>");
        }
    }

    // "85%" of the slot, or a width in pixels.
    private static (double Fraction, double Pixels) Share(string? width, double fallback)
    {
        if (string.IsNullOrWhiteSpace(width))
        {
            return (fallback, 0);
        }

        var text = width.AsSpan().Trim();
        return text[^1] == '%'
            ? (double.Parse(text[..^1], CultureInfo.InvariantCulture) / 100, 0)
            : (0, double.Parse(text.TrimEnd("px"), CultureInfo.InvariantCulture));
    }

    // Flux's "4 0": the value end's radius, then the base's. The base of a bar stands on the axis and stays square.
    private static double Radius(string? radius, double fallback)
    {
        if (string.IsNullOrWhiteSpace(radius))
        {
            return fallback;
        }

        var text = radius.AsSpan().Trim();
        var space = text.IndexOf(' ');
        return double.Parse(space < 0 ? text : text[..space], CultureInfo.InvariantCulture);
    }

    private void Dashes(string? dashes)
    {
        if (dashes is not null)
        {
            _svg.Append(" stroke-dasharray=\"").Append(WebUtility.HtmlEncode(dashes)).Append('"');
        }
    }

    private void Class(string? look, string? own)
    {
        if (look is null && string.IsNullOrWhiteSpace(own))
        {
            return;
        }

        _svg.Append(" class=\"").Append(look);
        if (!string.IsNullOrWhiteSpace(own))
        {
            _svg.Append(look is null ? "" : " ").Append(WebUtility.HtmlEncode(own.Trim()));
        }

        _svg.Append('"');
    }
}
