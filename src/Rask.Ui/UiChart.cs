namespace Rask;

/// <summary>
/// A chart of rows, drawn as SVG where the page is rendered. Flux UI's chart.
/// </summary>
/// <remarks>
/// <para>
/// Assembled from parts, as Flux's is: a <see cref="UiChartSvg" /> holding the lines, bars and axes, and beside
/// it a tooltip, a summary or a legend. Size it with a class, <c>aspect-3/1</c> or <c>h-64</c>.
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
/// Flux draws in the browser, from script. This is drawn in C#, with no script of its own: the scales, the
/// ticks and every path are computed during the render, to the numbers Flux's own layout arrives at.
/// </para>
/// </remarks>
public sealed partial class UiChart : Component
{
    private static readonly UiPartMarker Marker = new("ui-chart");

    /// <summary>The rows: <c>Ui.Chart.Value(rows)</c>, one point, bar or slice per row. Flux's <c>wire:model</c> and <c>:value</c>.</summary>
    public UiChartData? Value { get; set; }

    /// <summary>Puts the rows down the Y axis and their values along the X axis: bars lying on their side.</summary>
    public bool? Horizontal { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        UiChartTooltip? tooltip = null;
        foreach (var child in Children ?? [])
        {
            tooltip ??= child as UiChartTooltip;
        }

        return Div.Class(UiClass.Compose("relative block", Class)).Data(Marker.With(null))[
            Context.Provide(new UiChartScope(Value, Horizontal == true, tooltip))[Children ?? []]
        ];
    }
}
