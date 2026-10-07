namespace Rask;

/// <summary>
/// The label at each tick of the axis it is declared in. Flux UI's <c>chart.axis.tick</c>.
/// </summary>
public sealed partial class UiChartAxisTick : Component
{
    /// <summary>How the labels are written. The axis's own format unless this says otherwise.</summary>
    public UiChartFormat? Format { get; set; }

    public string? Class { get; set; }

    // Drawn by the UiChartSvg it is declared in, which lays every part out on one set of scales.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
