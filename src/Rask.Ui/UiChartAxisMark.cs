namespace Rask;

/// <summary>
/// A short line at each tick of the axis it is declared in. Flux UI's <c>chart.axis.mark</c>.
/// </summary>
public sealed partial class UiChartAxisMark : Component
{
    /// <summary>Which side of the plot the marks sit on. The axis's own side unless this says otherwise.</summary>
    public Ui.Position? Position { get; set; }

    /// <summary>The <c>stroke-width</c> of the SVG element it draws.</summary>
    public double? StrokeWidth { get; set; }

    /// <summary>The <c>stroke-dasharray</c> of the SVG element it draws: <c>"4 4"</c>.</summary>
    public string? StrokeDasharray { get; set; }

    public string? Class { get; set; }

    // Drawn by the UiChartSvg it is declared in, which lays every part out on one set of scales.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
