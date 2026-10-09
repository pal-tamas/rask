namespace Rask;

/// <summary>One row of a chart's tooltip: a label and the row's number. Flux UI's <c>chart.tooltip.value</c>.</summary>
/// <remarks>What is put inside comes before the label: a <see cref="UiChartTooltipIndicator" />, for a pie.</remarks>
public sealed partial class UiChartTooltipValue : Component, IUiChartField, IUiChartLabelField
{
    /// <summary>The number shown: <c>.Field((Visit v) =&gt; v.Visitors)</c>.</summary>
    public UiChartField? Field { get; set; }

    /// <summary>How it is written.</summary>
    public UiChartFormat? Format { get; set; }

    /// <summary>What comes before the number: a series' name.</summary>
    public string? Label { get; set; }

    /// <summary>A label read from the row instead: the hovered slice's name, on a pie.</summary>
    public UiChartField? LabelField { get; set; }

    /// <summary>Written before the number: <c>"$"</c>.</summary>
    public string? Prefix { get; set; }

    /// <summary>Written after the number: <c>"%"</c>.</summary>
    public string? Suffix { get; set; }

    public string? Class { get; set; }

    private const string Look = "flex items-center gap-2 p-2 text-xs text-zinc-500 dark:text-zinc-300";
    private const string Name = "text-zinc-800 dark:text-white";

    private readonly UiChartLines _labels = new();
    private readonly UiChartLines _values = new();

    // What it reads is the chart's rows, which arrive through the context rather than as a prop of its own.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    // At rest the number is missing, as Flux's: the plot hook writes the hovered row's line into each part.
    /// <inheritdoc />
    protected override Component? Render()
    {
        var data = Context.Get<UiChartScope>()?.Data;
        return Div.Class(UiClass.Compose(Look, Class))[
            Children ?? [],
            LabelField is { } label
                ? Div.Class(Name).Data(_labels.Of(data, label, format: null, asText: true))
                : Div.Class(Name)[Label],
            Div.Class("grow"),
            Div.Data(_values.Of(data, Field, Format, Prefix, Suffix))[Prefix + Suffix]
        ];
    }
}
