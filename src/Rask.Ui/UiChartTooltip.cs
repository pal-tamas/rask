namespace Rask;

/// <summary>
/// What a chart says about the row under the pointer: a <see cref="UiChartTooltipHeading" /> and a
/// <see cref="UiChartTooltipValue" /> per number. Flux UI's <c>chart.tooltip</c>.
/// </summary>
public sealed partial class UiChartTooltip : Component, IUiChartField
{
    /// <summary>The field the tooltip shows, when it holds no parts of its own.</summary>
    public UiChartField? Field { get; set; }

    /// <summary>How that field is written.</summary>
    public UiChartFormat? Format { get; set; }

    public string? Class { get; set; }

    internal const string Box =
        "pointer-events-none absolute flex flex-col overflow-hidden rounded-lg border border-zinc-200 "
        + "bg-white shadow-lg dark:border-zinc-500 dark:bg-zinc-700";

    // At rest it is in the document and invisible, as Flux leaves it until the pointer arrives.
    /// <inheritdoc />
    protected override Component? Render() => Div.Class(UiClass.Compose(Box, "opacity-0", Class))[Children ?? []];
}
