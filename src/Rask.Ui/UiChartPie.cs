namespace Rask;

/// <summary>
/// A pie, or a donut once it has an inner radius: one slice per row, clockwise from twelve o'clock. Flux UI's <c>chart.pie</c>.
/// </summary>
public sealed partial class UiChartPie : Component, IUiChartField, IUiChartLabelField
{
    /// <summary>The number that sets each slice's size: <c>.Field((Share s) =&gt; s.Value)</c>.</summary>
    public UiChartField? Field { get; set; }

    /// <summary>What each slice is called, in the tooltip.</summary>
    public UiChartField? LabelField { get; set; }

    /// <summary>
    /// The hue of each slice, where a row chooses its own: <c>.ColorField((Share s) =&gt; s.Color)</c> returning a
    /// <see cref="Ui.Color" />. Flux reads this from a <c>color</c> key on the row. The built-in palette in turn otherwise.
    /// </summary>
    public UiChartField? ColorField { get; set; }

    /// <summary>The radius of the donut's hole, in pixels or as a percentage of the outer radius: <c>"60%"</c>.</summary>
    public string? InnerRadius { get; set; }

    /// <summary>How round each slice's corners are, in pixels.</summary>
    public double? Radius { get; set; }

    public string? Class { get; set; }

    // Drawn by the UiChartSvg it is declared in, which lays every part out on one set of scales.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
