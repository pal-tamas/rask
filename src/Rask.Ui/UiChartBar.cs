namespace Rask;

/// <summary>
/// A bar per row. Several stand side by side in a <see cref="UiChartGroup" /> or on top of one another in a <see cref="UiChartStack" />. Flux UI's <c>chart.bar</c>.
/// </summary>
public sealed partial class UiChartBar : Component, IUiChartField
{
    /// <summary>What the part reads from a row: <c>.Field((Visit v) =&gt; v.Visitors)</c>. The row itself when the chart holds bare numbers.</summary>
    public UiChartField? Field { get; set; }

    /// <summary>How round the bar's corners are, as Flux writes it: <c>"4"</c>, or <c>"4 0"</c> for the value end and then the base.</summary>
    public string? Radius { get; set; }

    /// <summary>How much of its slot the bar fills, as a percentage or in pixels: <c>"85%"</c>.</summary>
    public string? Width { get; set; }

    public string? Class { get; set; }

    // Drawn by the UiChartSvg it is declared in, which lays every part out on one set of scales.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
