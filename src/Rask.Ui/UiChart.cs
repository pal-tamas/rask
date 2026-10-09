namespace Rask;

/// <summary>
/// A chart of rows, drawn as SVG where the page is rendered. Flux UI's chart.
/// </summary>
/// <remarks>
/// <para>
/// Assembled from parts, as Flux's is: a <see cref="UiChartSvg" /> holding the lines, bars and axes, and beside
/// it a tooltip, a summary or a legend. Size it with a class, <c>aspect-3/1</c> or <c>h-64</c>: the drawing fills
/// that box and is drawn again whenever the box changes.
/// </para>
/// <code>
/// Ui.Chart.Value(visits).Class("aspect-3/1")[
///     Ui.ChartSvg[
///         Ui.ChartLine.Field((Visit v) =&gt; v.Visitors).Class("text-pink-500"),
///         Ui.ChartAxis.X.Field((Visit v) =&gt; v.Date)[Ui.ChartAxisLine, Ui.ChartAxisTick],
///         Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick],
///         Ui.ChartCursor],
///     Ui.ChartTooltip[
///         Ui.ChartTooltipHeading.Field((Visit v) =&gt; v.Date),
///         Ui.ChartTooltipValue.Field((Visit v) =&gt; v.Visitors).Label("Visitors")]]
/// </code>
/// <para>
/// Flux draws in the browser, from script. This is drawn in C#: the scales, the ticks and every path are
/// computed during the render, to the numbers Flux's own layout arrives at. What follows the pointer — the
/// cursor, the tooltip, a summary, a pie's active slice — is the runtime's plot hook, with no round trip.
/// </para>
/// </remarks>
public sealed partial class UiChart : Component
{
    // The second mark asks the runtime for its plot hook: the rows' places are the drawing's to state.
    private static readonly UiPartMarker Marker = new UiPartMarker("ui-chart").And("rask-plot", "");

    /// <summary>The rows: <c>Ui.Chart.Value(rows)</c>, one point, bar or slice per row. Flux's <c>wire:model</c> and <c>:value</c>.</summary>
    public UiChartData? Value { get; set; }

    /// <summary>Puts the rows down the Y axis and their values along the X axis: bars lying on their side.</summary>
    public bool? Horizontal { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("relative block", Class)).Data(Marker.With(null))[
            Context.Provide(new UiChartScope(Value, Horizontal == true, Pie(Children)))[Children ?? []]
        ];

    // The pie is declared inside the drawing, and the tooltip beside it names the hovered slice's colour.
    private static UiChartPie? Pie(IEnumerable<Component?>? parts)
    {
        foreach (var part in parts ?? [])
        {
            var pie = part switch
            {
                UiChartPie found => found,
                UiChartViewport or UiChartSvg => Pie(part.Children),
                _ => null,
            };
            if (pie is not null)
            {
                return pie;
            }
        }

        return null;
    }
}
