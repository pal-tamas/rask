namespace Rask;

/// <summary>
/// The chart's data binding: <c>Ui.Chart.Value(rows)</c>, and <c>.Field((Row r) =&gt; r.Member)</c> on each part.
/// </summary>
/// <remarks>
/// Flux's chart takes an array of rows (<c>wire:model</c> or <c>:value</c>) and its parts name a column with a
/// string. Here the chart takes the typed list and a part takes a selector, whose parameter type is stated once
/// in the lambda — <c>(Visit v) =&gt; v.Visitors</c> — because a part is built before the chart it goes into and
/// has nothing to infer the row type from. A misspelt member is then a compile error rather than an empty chart.
/// </remarks>
public static class UiChartBinding
{
    /// <summary>The rows the chart draws, one point, bar or slice per row, in order.</summary>
    public static UiChart Value<TRow>(this UiChart chart, IEnumerable<TRow> rows)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(rows);
        chart.Value = new UiChartRows<TRow>(rows as IReadOnlyList<TRow> ?? [.. rows]);
        return chart;
    }

    /// <summary>What the part reads from a row: a number to plot, or the date or name an axis shows.</summary>
    public static TPart Field<TPart, TRow, TValue>(this TPart part, Func<TRow, TValue> field)
        where TPart : IUiChartField
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(field);
        part.Field = new UiChartSelector<TRow, TValue>(field);
        return part;
    }

    /// <summary>What names a row: a pie slice's label, or the label of a tooltip row.</summary>
    public static TPart LabelField<TPart, TRow, TValue>(this TPart part, Func<TRow, TValue> field)
        where TPart : IUiChartLabelField
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(field);
        part.LabelField = new UiChartSelector<TRow, TValue>(field);
        return part;
    }

    /// <summary>The hue of each slice, where a row chooses its own.</summary>
    public static UiChartPie ColorField<TRow>(this UiChartPie pie, Func<TRow, Ui.Color> field)
    {
        ArgumentNullException.ThrowIfNull(pie);
        ArgumentNullException.ThrowIfNull(field);
        pie.ColorField = new UiChartSelector<TRow, Ui.Color>(field);
        return pie;
    }
}
