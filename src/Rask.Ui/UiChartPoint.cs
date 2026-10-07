namespace Rask;

/// <summary>
/// A dot on each row's value, drawn over a line or an area. Flux UI's <c>chart.point</c>.
/// </summary>
public sealed partial class UiChartPoint : Component, IUiChartField
{
    /// <summary>What the part reads from a row: <c>.Field((Visit v) =&gt; v.Visitors)</c>. The row itself when the chart holds bare numbers.</summary>
    public UiChartField? Field { get; set; }

    /// <summary>The circle's <c>r</c>, in pixels. 4 unless this says otherwise.</summary>
    public double? R { get; set; }

    /// <summary>The circle's <c>stroke-width</c>: the ring in the surface colour that lifts a dot off its line.</summary>
    public double? StrokeWidth { get; set; }

    public string? Class { get; set; }

    // Drawn by the UiChartSvg it is declared in, which lays every part out on one set of scales.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
