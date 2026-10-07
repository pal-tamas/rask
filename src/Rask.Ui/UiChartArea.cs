namespace Rask;

/// <summary>
/// The area under a line, filled. Give it the same field and curve as its <see cref="UiChartLine" />. Flux UI's <c>chart.area</c>.
/// </summary>
public sealed partial class UiChartArea : Component, IUiChartField
{
    /// <summary>What the part reads from a row: <c>.Field((Visit v) =&gt; v.Visitors)</c>. The row itself when the chart holds bare numbers.</summary>
    public UiChartField? Field { get; set; }

    /// <summary>How the points are joined. Smooth unless this says otherwise.</summary>
    public Ui.ChartCurve? Curve { get; set; }

    public string? Class { get; set; }

    // Drawn by the UiChartSvg it is declared in, which lays every part out on one set of scales.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
