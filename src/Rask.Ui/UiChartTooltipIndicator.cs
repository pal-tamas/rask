namespace Rask;

/// <summary>
/// A dot in the colour of the hovered pie slice, put inside a <see cref="UiChartTooltipValue" />.
/// Flux UI's <c>chart.tooltip.indicator</c>.
/// </summary>
public sealed partial class UiChartTooltipIndicator : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() => Div.Class(UiClass.Compose(
        "size-2.5 rounded-full",
        Context.Get<UiChartHue>()?.Color is { } hue ? UiChartPieDrawing.Background(hue) : null,
        Class));
}
