namespace Rask;

/// <summary>
/// The baseline of the axis it is declared in. Flux UI's <c>chart.axis.line</c>.
/// </summary>
public sealed partial class UiChartAxisLine : Component
{
    /// <summary>The <c>stroke-width</c> of the SVG element it draws.</summary>
    public double? StrokeWidth { get; set; }

    public string? Class { get; set; }

    // Drawn by the UiChartSvg it is declared in, which lays every part out on one set of scales.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
