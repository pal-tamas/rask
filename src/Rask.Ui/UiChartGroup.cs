namespace Rask;

/// <summary>
/// Bars side by side for each row: put the <see cref="UiChartBar" /> parts inside. Flux UI's <c>chart.group</c>.
/// </summary>
public sealed partial class UiChartGroup : Component
{
    /// <summary>How much of a row's slot the group fills: <c>"90%"</c>.</summary>
    public string? Width { get; set; }

    public string? Class { get; set; }

    // Drawn by the UiChartSvg it is declared in, which lays every part out on one set of scales.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
