namespace Rask;

/// <summary>
/// Bars stacked on one another for each row: put the <see cref="UiChartBar" /> parts inside, bottom first. Flux UI's <c>chart.stack</c>.
/// </summary>
#pragma warning disable CA1711 // Flux's part is chart.stack, and every part is named after Flux's
public sealed partial class UiChartStack : Component
#pragma warning restore CA1711
{
    /// <summary>How much of a row's slot the stack fills: <c>"65%"</c>.</summary>
    public string? Width { get; set; }

    public string? Class { get; set; }

    // Drawn by the UiChartSvg it is declared in, which lays every part out on one set of scales.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
