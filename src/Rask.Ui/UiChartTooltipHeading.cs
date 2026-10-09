namespace Rask;

/// <summary>The line at the top of a chart's tooltip: usually the row's date or name. Flux UI's <c>chart.tooltip.heading</c>.</summary>
public sealed partial class UiChartTooltipHeading : Component, IUiChartField
{
    /// <summary>What the heading shows: <c>.Field((Visit v) =&gt; v.Date)</c>.</summary>
    public UiChartField? Field { get; set; }

    /// <summary>How it is written.</summary>
    public UiChartFormat? Format { get; set; }

    public string? Class { get; set; }

    private const string Look =
        "flex items-center justify-between border-b border-zinc-200 bg-zinc-50 p-2 text-xs font-medium text-zinc-800 "
        + "dark:border-zinc-500 dark:bg-zinc-600 dark:text-zinc-100";

    private readonly UiChartLines _lines = new();

    // What it reads is the chart's rows, which arrive through the context rather than as a prop of its own.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    // Empty at rest, as Flux's: the plot hook writes the hovered row's line into it.
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(Look, Class)).Data(_lines.Of(Context.Get<UiChartScope>()?.Data, Field, Format));
}
