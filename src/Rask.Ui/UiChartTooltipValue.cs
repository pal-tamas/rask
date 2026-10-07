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

    /// <inheritdoc />
    protected override Component? Render() => Div.Class(UiClass.Compose(Look, Class))[
        Children ?? [],
        Div.Class(Name)[Label],
        Div.Class("grow"),
        Div[Prefix + Suffix]
    ];

    /// <summary>The row as it reads for one row of data; <paramref name="hue" /> colours an indicator inside it.</summary>
    internal Component For(UiChartData data, int row, Ui.Color? hue) => Div.Class(UiClass.Compose(Look, Class))[
        Context.Provide(new UiChartHue(hue))[Children ?? []],
        Div.Class(Name)[LabelField is null ? Label : LabelField.Text(data, row)],
        Div.Class("grow"),
        Div[Prefix + UiChartWriting.Value(Field, Format, data, row) + Suffix]
    ];
}
