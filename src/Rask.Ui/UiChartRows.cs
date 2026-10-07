namespace Rask;

/// <summary>The rows of a chart, as the typed list a field's selector reads.</summary>
internal sealed class UiChartRows<TRow>(IReadOnlyList<TRow> rows) : UiChartData
{
    internal IReadOnlyList<TRow> Rows { get; } = rows;

    internal override int Count => Rows.Count;

    internal override double Number(int row) => UiChartConvert<TRow>.Number(Rows[row]);
}
