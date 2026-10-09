using System.Globalization;
using System.Text;
using static Rask.Markup;

namespace Rask;

/// <summary>
/// What a drawing asks of the runtime's plot hook: its own size, and where its rows are under the pointer.
/// </summary>
/// <remarks>
/// <para>
/// Flux measures its element and follows the pointer in script. Here one layer over the drawing does both by
/// attribute. It is the measured box (<c>data-rask-measure</c>, with the one bound field the size comes back
/// in) and the box the tooltip is kept inside (<c>data-rask-plot-frame</c>), and it holds the plot area (<c>data-rask-plot-area</c>): the region that answers the pointer, each
/// row's place along it, and the ONE cursor, which the hook moves with a custom property.
/// </para>
/// <para>
/// Everything is placed in percentages of the drawing's box, so it stays on its row while the SVG is still
/// scaled from a size it was not measured at. Flux has no node like this one: the parity tool knows it by its
/// marker.
/// </para>
/// </remarks>
internal static class UiChartHover
{
    private static readonly UiPartMarker Marker = new UiPartMarker("ui-chart-hover").And("rask-measure", "").And("rask-plot-frame", "");

    private const string Layer = "pointer-events-none absolute inset-0";

    // Unseen until the hook marks the chart active; drawn in Flux's cursor colour, dashed four on, four off.
    private const string Cursor = "absolute hidden text-zinc-500 in-data-active:block dark:text-zinc-300";
    private const string Upright = "inset-y-0 w-px -translate-x-1/2 bg-[repeating-linear-gradient(to_top,currentColor_0_4px,transparent_4px_8px)]";
    private const string Level = "inset-x-0 h-px -translate-y-1/2 bg-[repeating-linear-gradient(to_right,currentColor_0_4px,transparent_4px_8px)]";
    private const string Band = "border border-dashed border-current opacity-10";

    /// <summary>
    /// The marks of a node the kit adds for one row — Flux has ONE such node and recolours it by script — which
    /// the hook lights while that row is active.
    /// </summary>
    internal static IReadOnlyDictionary<string, string?> Row(int row) =>
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-chart-hover"] = null, ["rask-plot-row"] = row.ToString(CultureInfo.InvariantCulture) };

    /// <summary>The layer over a drawing of <paramref name="size" />: the field it is measured into, and <paramref name="area" />.</summary>
    internal static Component Over(string size, Action<string> measured, Component? area) =>
        Div.Class(Layer).Data(Marker.With(null))[
            Input.Value(size).OnInput(measured).Type(InputType.Hidden),
            area
        ];

    /// <summary>Where a plot's own box is in its drawing, and each row's place along it: written once per drawing.</summary>
    internal static (string Box, IReadOnlyDictionary<string, string?> Marks)? Place(UiChartPlot plot)
    {
        if (plot.Units.Length == 0)
        {
            return null;
        }

        var box = string.Create(CultureInfo.InvariantCulture,
            $"left:{Percent(plot.Left / plot.Width)};top:{Percent(plot.Top / plot.Height)};width:{Percent((plot.Right - plot.Left) / plot.Width)};height:{Percent((plot.Bottom - plot.Top) / plot.Height)}");
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal) { ["rask-plot-area"] = Places(plot.Units) };
        if (plot.Horizontal)
        {
            marks["rask-plot-axis"] = "y";
        }

        return (box, marks);
    }

    /// <summary>The plot's own box, with the cursor inside.</summary>
    internal static Component Area((string Box, IReadOnlyDictionary<string, string?> Marks) place, UiChartPlot plot, UiChartParts parts) =>
        Div.Class("absolute").Style(place.Box).Data(place.Marks)[Line(plot, parts)];

    private static string Places(double[] units)
    {
        var places = new StringBuilder(units.Length * 8);
        foreach (var unit in units)
        {
            places.Append(places.Length == 0 ? "" : " ").Append(unit.ToString("0.#####", CultureInfo.InvariantCulture));
        }

        return places.ToString();
    }

    // One cursor for every row: `--rask-plot-at` is the active row's place along the area, set by the hook.
    private static HTMLDivElement? Line(UiChartPlot plot, UiChartParts parts)
    {
        if (parts.Cursor is not { } cursor)
        {
            return null;
        }

        const string At = "calc(var(--rask-plot-at, 0) * 100%)";
        if (cursor.Type != Ui.ChartCursorType.Area)
        {
            return plot.Horizontal
                ? Div.Class(UiClass.Compose(Cursor, Level, cursor.Class)).Style("top:" + At)
                : Div.Class(UiClass.Compose(Cursor, Upright, cursor.Class)).Style("left:" + At);
        }

        // Flux's area cursor covers the row's whole band, however much of it the bar fills.
        var across = Percent(plot.Band);
        return plot.Horizontal
            ? Div.Class(UiClass.Compose(Cursor, Band, "inset-x-0 -translate-y-1/2", cursor.Class)).Style($"top:{At};height:{across}")
            : Div.Class(UiClass.Compose(Cursor, Band, "inset-y-0 -translate-x-1/2", cursor.Class)).Style($"left:{At};width:{across}");
    }

    private static string Percent(double fraction) => (fraction * 100).ToString("0.###", CultureInfo.InvariantCulture) + "%";
}
