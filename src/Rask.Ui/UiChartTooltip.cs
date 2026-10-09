namespace Rask;

/// <summary>
/// What a chart says about the row under the pointer: a <see cref="UiChartTooltipHeading" /> and a
/// <see cref="UiChartTooltipValue" /> per number. Flux UI's <c>chart.tooltip</c>.
/// </summary>
/// <remarks>
/// One box for every row. It rests in the document unseen; under the pointer the runtime's plot hook moves it
/// beside the row, 15px off, to whichever side keeps it inside the chart, and its parts read that row.
/// </remarks>
public sealed partial class UiChartTooltip : Component, IUiChartField
{
    /// <summary>The field the tooltip shows, when it holds no parts of its own.</summary>
    public UiChartField? Field { get; set; }

    /// <summary>How that field is written.</summary>
    public UiChartFormat? Format { get; set; }

    public string? Class { get; set; }

    private const string Box =
        "pointer-events-none absolute flex flex-col overflow-hidden rounded-lg border border-zinc-200 "
        + "bg-white opacity-0 shadow-lg data-active:opacity-100 dark:border-zinc-500 dark:bg-zinc-700";

    // How far from the row and from the pointer Flux keeps it.
    private static readonly IReadOnlyDictionary<string, string?> Beside =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["rask-plot-tooltip"] = "15" };

    /// <inheritdoc />
    protected override Component? Render() => Div.Class(UiClass.Compose(Box, Class)).Data(Beside)[Children ?? []];
}
