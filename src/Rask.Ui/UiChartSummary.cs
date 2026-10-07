namespace Rask;

/// <summary>
/// Figures shown beside a chart, usually above it: put <see cref="UiChartSummaryValue" /> parts anywhere inside.
/// Flux UI's <c>chart.summary</c>.
/// </summary>
public sealed partial class UiChartSummary : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() => Div.Class(Class)[Children ?? []];
}
