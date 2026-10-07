using System.Globalization;

namespace Rask;

/// <summary>A <see cref="UiChartField" /> over a selector: the member of <typeparamref name="TRow" /> a part draws.</summary>
internal sealed class UiChartSelector<TRow, TValue>(Func<TRow, TValue> selector) : UiChartField
{
    internal override UiChartFieldKind Kind => UiChartConvert<TValue>.Kind;

    internal override double Number(UiChartData data, int row) => UiChartConvert<TValue>.Number(Read(data, row));

    internal override DateTime Time(UiChartData data, int row) => UiChartConvert<TValue>.Time(Read(data, row));

    internal override string Text(UiChartData data, int row) => Read(data, row) switch
    {
        null => string.Empty,
        IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
        var value => value.ToString() ?? string.Empty,
    };

    internal override object? Raw(UiChartData data, int row) => Read(data, row);

    private TValue Read(UiChartData data, int row) => data is UiChartRows<TRow> rows
        ? selector(rows.Rows[row])
        : throw new InvalidOperationException(
            $"A chart part reads {typeof(TRow).Name} rows, and the chart it is in holds something else. "
            + "Give the lambda the chart's row type: .Field((Visit v) => v.Visitors).");
}
