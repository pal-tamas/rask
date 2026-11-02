namespace Rask;

/// <summary>
/// Bars side by side for each row: put the <see cref="UiChartBar" /> parts inside. Flux UI's <c>chart.group</c>.
/// </summary>
/// <remarks>
/// It fills nine tenths of a row's slot, as Flux's does. It takes no <c>width</c>: Flux's examples set one on a
/// <see cref="UiChartStack" /> and a <see cref="UiChartBar" />, never on a group.
/// </remarks>
public sealed partial class UiChartGroup : Component
{
    public string? Class { get; set; }

    // Drawn by the UiChartSvg it is declared in, which lays every part out on one set of scales.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
