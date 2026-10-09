using System.Globalization;

namespace Rask;

/// <summary>
/// What one part of a chart reads for EVERY row, kept for as long as what it is written from stays the same.
/// </summary>
/// <remarks>
/// A part that reads the chart's rows from the context is rendered on every walk of the page, changed or not —
/// and its lines are a formatted value per row. The rows, the field and the format are compared by identity: a
/// page that renders again hands over new ones, and one that does not cannot have changed them.
/// </remarks>
internal sealed class UiChartLines
{
    private (UiChartData? Data, UiChartField? Field, UiChartFormat? Format, string? Prefix, string? Suffix, bool AsText, CultureInfo Culture) _from;
    private IReadOnlyDictionary<string, string?>? _rows;

    /// <summary>
    /// The <c>data-rask-plot-text</c> mark: <paramref name="field" /> of each row, a row per line, written with
    /// <paramref name="format" /> between <paramref name="prefix" /> and <paramref name="suffix" /> — or, with
    /// <paramref name="asText" />, as the row holds it.
    /// </summary>
    internal IReadOnlyDictionary<string, string?> Of(
        UiChartData? data, UiChartField? field, UiChartFormat? format, string? prefix = null, string? suffix = null, bool asText = false)
    {
        var from = (data, field, format, prefix, suffix, asText, CultureInfo.CurrentCulture);
        if (_rows is null || !Same(from))
        {
            _from = from;
            _rows = UiChartWriting.Rows(data, (rows, row) =>
                prefix + (asText && field is not null ? field.Text(rows, row) : UiChartWriting.Value(field, format, rows, row)) + suffix);
        }

        return _rows;
    }

    private bool Same((UiChartData? Data, UiChartField? Field, UiChartFormat? Format, string? Prefix, string? Suffix, bool AsText, CultureInfo Culture) from) =>
        ReferenceEquals(from.Data, _from.Data) && ReferenceEquals(from.Field, _from.Field) && ReferenceEquals(from.Format, _from.Format)
        && string.Equals(from.Prefix, _from.Prefix, StringComparison.Ordinal) && string.Equals(from.Suffix, _from.Suffix, StringComparison.Ordinal)
        && from.AsText == _from.AsText && ReferenceEquals(from.Culture, _from.Culture);
}
