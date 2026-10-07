namespace Rask;

/// <summary>
/// The guide that marks the row under the pointer, and what makes the tooltip follow it. Flux UI's <c>chart.cursor</c>.
/// </summary>
public sealed partial class UiChartCursor : Component
{
    /// <summary>A line through the row, or the row's whole band shaded. A line unless this says otherwise.</summary>
    public Ui.ChartCursorType? Type { get; set; }

    /// <summary>The <c>stroke-width</c> of the SVG element it draws.</summary>
    public double? StrokeWidth { get; set; }

    /// <summary>The <c>stroke-dasharray</c> of the SVG element it draws: <c>"4 4"</c>.</summary>
    public string? StrokeDasharray { get; set; }

    public string? Class { get; set; }

    // Drawn by the UiChartSvg it is declared in, which lays every part out on one set of scales.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
