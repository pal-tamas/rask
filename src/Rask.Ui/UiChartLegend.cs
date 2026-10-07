namespace Rask;

/// <summary>
/// One entry of a chart's legend: a <see cref="UiChartLegendIndicator" /> inside, and the series' name.
/// Flux UI's <c>chart.legend</c>.
/// </summary>
public sealed partial class UiChartLegend : Component, IUiChartField
{
    /// <summary>The field this entry stands for.</summary>
    public UiChartField? Field { get; set; }

    /// <summary>The series' name.</summary>
    public string? Label { get; set; }

    /// <summary>How the field's value is written.</summary>
    public UiChartFormat? Format { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() => Div.Class(UiClass.Compose("flex items-center gap-2 p-2", Class))[
        Children ?? [],
        Div.Class("text-xs text-zinc-500 dark:text-zinc-400")[Label]
    ];
}
