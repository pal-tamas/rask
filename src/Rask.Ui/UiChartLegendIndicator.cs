namespace Rask;

/// <summary>
/// The dot beside a legend entry: give it the series' colour, <c>.Class("bg-blue-400")</c>.
/// Flux UI's <c>chart.legend.indicator</c>.
/// </summary>
public sealed partial class UiChartLegendIndicator : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() => Div.Class(UiClass.Compose("size-2.5 rounded-full", Class));
}
