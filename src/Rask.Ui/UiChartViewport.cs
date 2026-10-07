namespace Rask;

/// <summary>
/// The box a chart's SVG is drawn in, when the chart holds more than the drawing: a legend under it, a summary
/// over it, or the middle of a donut. Flux UI's <c>chart.viewport</c>.
/// </summary>
public sealed partial class UiChartViewport : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() => Div.Class(UiClass.Compose("relative", Class))[Children ?? []];
}
