namespace Rask;

/// <summary>
/// A line through each row's value. Flux UI's <c>chart.line</c>.
/// </summary>
public sealed partial class UiChartLine : Component, IUiChartField
{
    /// <summary>What the part reads from a row: <c>.Field((Visit v) =&gt; v.Visitors)</c>. The row itself when the chart holds bare numbers.</summary>
    public UiChartField? Field { get; set; }

    /// <summary>How the points are joined. Smooth unless this says otherwise.</summary>
    public Ui.ChartCurve? Curve { get; set; }

    /// <summary>The <c>stroke-dasharray</c> of the SVG element it draws: <c>"4 4"</c>.</summary>
    public string? StrokeDasharray { get; set; }

    public string? Class { get; set; }

    // Drawn by the UiChartSvg it is declared in, which lays every part out on one set of scales.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
