using System.Globalization;
using static Rask.Markup;

namespace Rask;

/// <summary>
/// What a chart does under the pointer, with no script: a strip per row over the drawing, which shows that row's
/// cursor and tooltip while the pointer is in it.
/// </summary>
/// <remarks>
/// <para>
/// Flux follows the pointer in script: it finds the nearest row, moves one cursor line and one tooltip there, and
/// rewrites the tooltip's text. The same nearest-row rule is here as geometry instead — each strip reaches halfway
/// to its neighbours, so the pointer is always in the strip of the row it is nearest — and every row carries its
/// own cursor and tooltip, already written, which <c>:hover</c> reveals.
/// </para>
/// <para>
/// Everything is placed in percentages of the drawing's box, so it stays on its row when the SVG scales. The
/// tooltip sits beside its row, on the side with more room; Flux's also follows the pointer up and down.
/// </para>
/// </remarks>
internal static class UiChartHover
{
    private const double Beside = 15;

    private static readonly UiPartMarker Marker = new("ui-chart-hover");

    private const string Line =
        "pointer-events-none absolute hidden text-zinc-500 group-hover/row:block dark:text-zinc-300";

    internal static Component? For(UiChartPlot plot, UiChartParts parts, UiChartData data, UiChartTooltip? tooltip)
    {
        if ((parts.Cursor is null && tooltip is null) || data.Count == 0)
        {
            return null;
        }

        var strips = new Component[data.Count];
        for (var row = 0; row < strips.Length; row++)
        {
            strips[row] = Strip(plot, parts, data, tooltip, row);
        }

        // Flux has no node like this one: it stands in for Flux's script, and the parity tool knows it by this marker.
        return Div.Class("absolute inset-0").Data(Marker.With(null))[strips];
    }

    private static Component Strip(UiChartPlot plot, UiChartParts parts, UiChartData data, UiChartTooltip? tooltip, int row)
    {
        var (from, to) = Reach(plot, row);
        var at = plot.Index(plot.Units[row]);
        var along = plot.Horizontal ? plot.Height : plot.Width;
        var across = plot.Horizontal ? plot.Width : plot.Height;
        var (near, far) = plot.Horizontal ? (plot.Left, plot.Width - plot.Right) : (plot.Top, plot.Height - plot.Bottom);
        // Where the row sits within its own strip, and which half of the chart it is in.
        var within = to > from ? (at - from) / (to - from) : 0.5;
        var late = at > along / 2;

        var strip = plot.Horizontal
            ? $"top:{Percent(from / along)};height:{Percent((to - from) / along)};left:{Percent(near / across)};right:{Percent(far / across)}"
            : $"left:{Percent(from / along)};width:{Percent((to - from) / along)};top:{Percent(near / across)};bottom:{Percent(far / across)}";

        return Div.Key(row).Class("group/row absolute").Style(strip)[
            Cursor(plot, parts.Cursor, within),
            tooltip is null ? null : Tooltip(plot, tooltip, data, row, within, late)
        ];
    }

    // Halfway to the row before and to the row after; a band's own edges under bars; the plot's edge at either end.
    private static (double From, double To) Reach(UiChartPlot plot, int row)
    {
        var units = plot.Units;
        var half = plot.Band / 2;
        var from = plot.Banded ? units[row] - half : Between(units, row - 1, row, 0);
        var to = plot.Banded ? units[row] + half : Between(units, row, row + 1, 1);
        return (plot.Index(Math.Min(from, to)), plot.Index(Math.Max(from, to)));
    }

    private static double Between(double[] units, int a, int b, double edge) =>
        a < 0 || b >= units.Length ? edge : (units[a] + units[b]) / 2;

    private static HTMLDivElement? Cursor(UiChartPlot plot, UiChartCursor? cursor, double within)
    {
        if (cursor is null)
        {
            return null;
        }

        if (cursor.Type == Ui.ChartCursorType.Area)
        {
            return Div.Class(UiClass.Compose(Line, "inset-0 border border-dashed border-current", cursor.Class));
        }

        return plot.Horizontal
            ? Div.Class(UiClass.Compose(Line, "inset-x-0 h-px bg-[repeating-linear-gradient(to_right,currentColor_0_4px,transparent_4px_8px)]", cursor.Class))
                .Style($"top:{Percent(within)}")
            : Div.Class(UiClass.Compose(Line, "inset-y-0 w-px bg-[repeating-linear-gradient(to_top,currentColor_0_4px,transparent_4px_8px)]", cursor.Class))
                .Style($"left:{Percent(within)}");
    }

    private static Component Tooltip(UiChartPlot plot, UiChartTooltip tooltip, UiChartData data, int row, double within, bool late)
    {
        var rows = new List<Component?>();
        foreach (var child in tooltip.Children ?? [])
        {
            rows.Add(child switch
            {
                UiChartTooltipHeading heading => heading.For(data, row),
                UiChartTooltipValue value => value.For(data, row, hue: null),
                _ => child,
            });
        }

        var offset = Beside.ToString(CultureInfo.InvariantCulture);
        var place = (plot.Horizontal, late) switch
        {
            (false, false) => $"left:calc({Percent(within)} + {offset}px);top:40%",
            (false, true) => $"right:calc({Percent(1 - within)} + {offset}px);top:40%",
            (true, false) => $"top:calc({Percent(within)} + {offset}px);left:40%",
            (true, true) => $"bottom:calc({Percent(1 - within)} + {offset}px);left:40%",
        };

        return Div.Class(UiClass.Compose(UiChartTooltip.Box, "z-10 hidden w-max group-hover/row:flex", tooltip.Class)).Style(place)[rows];
    }

    private static string Percent(double fraction) => (fraction * 100).ToString("0.###", CultureInfo.InvariantCulture) + "%";
}
