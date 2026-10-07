namespace Rask;

/// <summary>
/// The chart's data binding: <c>Ui.Chart.Value(rows)</c>, and <c>.Field((Row r) =&gt; r.Member)</c> on each part.
/// </summary>
/// <remarks>
/// <para>
/// Flux's chart takes an array of rows (<c>wire:model</c> or <c>:value</c>) and its parts name a column with a
/// string. Here the chart takes the typed list and a part takes a selector, whose parameter type is stated once
/// in the lambda — <c>(Visit v) =&gt; v.Visitors</c> — because a part is built before the chart it goes into and
/// has nothing to infer the row type from. A misspelt member is then a compile error rather than an empty chart.
/// </para>
/// <para>
/// Each overload hands its value to the part's own generated chain step rather than setting the property: a
/// step is what a live page carries from one render to the next, and a property set behind its back is lost.
/// </para>
/// </remarks>
public static class UiChartBinding
{
    /// <summary>The rows the chart draws, one point, bar or slice per row, in order.</summary>
    public static UiChart Value<TRow>(this UiChart chart, IEnumerable<TRow> rows)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(rows);
        return chart.Value((UiChartData)new UiChartRows<TRow>(rows as IReadOnlyList<TRow> ?? [.. rows]));
    }

    /// <summary>What the part reads from a row: a number to plot, or the date or name it shows.</summary>
    public static UiChartLine Field<TRow, TValue>(this UiChartLine part, Func<TRow, TValue> field) => part.Field(Selector(field));

    /// <summary>What the part reads from a row: a number to plot, or the date or name it shows.</summary>
    public static UiChartArea Field<TRow, TValue>(this UiChartArea part, Func<TRow, TValue> field) => part.Field(Selector(field));

    /// <summary>What the part reads from a row: a number to plot, or the date or name it shows.</summary>
    public static UiChartPoint Field<TRow, TValue>(this UiChartPoint part, Func<TRow, TValue> field) => part.Field(Selector(field));

    /// <summary>What the part reads from a row: a number to plot, or the date or name it shows.</summary>
    public static UiChartBar Field<TRow, TValue>(this UiChartBar part, Func<TRow, TValue> field) => part.Field(Selector(field));

    /// <summary>What the part reads from a row: a number to plot, or the date or name it shows.</summary>
    public static UiChartPie Field<TRow, TValue>(this UiChartPie part, Func<TRow, TValue> field) => part.Field(Selector(field));

    /// <summary>What the part reads from a row: a number to plot, or the date or name it shows.</summary>
    public static UiChartAxis Field<TRow, TValue>(this UiChartAxis part, Func<TRow, TValue> field) => part.Field(Selector(field));

    /// <summary>What the part reads from a row: a number to plot, or the date or name it shows.</summary>
    public static UiChartTooltip Field<TRow, TValue>(this UiChartTooltip part, Func<TRow, TValue> field) => part.Field(Selector(field));

    /// <summary>What the part reads from a row: a number to plot, or the date or name it shows.</summary>
    public static UiChartTooltipHeading Field<TRow, TValue>(this UiChartTooltipHeading part, Func<TRow, TValue> field) => part.Field(Selector(field));

    /// <summary>What the part reads from a row: a number to plot, or the date or name it shows.</summary>
    public static UiChartTooltipValue Field<TRow, TValue>(this UiChartTooltipValue part, Func<TRow, TValue> field) => part.Field(Selector(field));

    /// <summary>What the part reads from a row: a number to plot, or the date or name it shows.</summary>
    public static UiChartSummaryValue Field<TRow, TValue>(this UiChartSummaryValue part, Func<TRow, TValue> field) => part.Field(Selector(field));

    /// <summary>What the part reads from a row: a number to plot, or the date or name it shows.</summary>
    public static UiChartLegend Field<TRow, TValue>(this UiChartLegend part, Func<TRow, TValue> field) => part.Field(Selector(field));

    /// <summary>What names a row: a pie slice's label, or the label of a tooltip row.</summary>
    public static UiChartPie LabelField<TRow, TValue>(this UiChartPie part, Func<TRow, TValue> field) => part.LabelField(Selector(field));

    /// <summary>What names a row: a pie slice's label, or the label of a tooltip row.</summary>
    public static UiChartTooltipValue LabelField<TRow, TValue>(this UiChartTooltipValue part, Func<TRow, TValue> field) => part.LabelField(Selector(field));

    /// <summary>The hue of each slice, where a row chooses its own.</summary>
    public static UiChartPie ColorField<TRow>(this UiChartPie pie, Func<TRow, Ui.Color> field) => pie.ColorField(Selector(field));

    private static UiChartSelector<TRow, TValue> Selector<TRow, TValue>(Func<TRow, TValue> field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new UiChartSelector<TRow, TValue>(field);
    }
}
