namespace Rask;

/// <summary>
/// A line at zero on the value axis, drawn only when the axis reaches below zero. Flux UI's <c>chart.zero-line</c>.
/// </summary>
public sealed partial class UiChartZeroLine : Component
{
    /// <summary>The <c>stroke-width</c> of the SVG element it draws.</summary>
    public double? StrokeWidth { get; set; }

    public string? Class { get; set; }

    // Drawn by the UiChartSvg it is declared in, which lays every part out on one set of scales.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
