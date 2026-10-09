using System.Globalization;
using System.Text;

namespace Rask;

/// <summary>How a row's value reads in a tooltip, a summary or a legend.</summary>
internal static class UiChartWriting
{
    internal static string Value(UiChartField? field, UiChartFormat? format, UiChartData data, int row)
    {
        if (field is null)
        {
            return Number(data.Number(row), format);
        }

        return field.Kind switch
        {
            UiChartFieldKind.Time => format is null ? Moment(field.Time(data, row)) : format.Date(field.Time(data, row)),
            UiChartFieldKind.Number => Number(field.Number(data, row), format),
            _ => field.Text(data, row),
        };
    }

    /// <summary>
    /// What an element reads for EVERY row, a row per line: the runtime's plot hook shows the line of the row
    /// under the pointer (<c>data-rask-plot-text</c>), so the page is not asked about each move.
    /// </summary>
    internal static IReadOnlyDictionary<string, string?> Rows(UiChartData? data, Func<UiChartData, int, string> written)
    {
        var lines = new StringBuilder((data?.Count ?? 0) * 8);
        for (var row = 0; row < (data?.Count ?? 0); row++)
        {
            var start = lines.Append(row == 0 ? "" : "\n").Length;
            // A line is one row: a break inside a value would shift every row after it.
            lines.Append(written(data!, row)).Replace('\n', ' ', start, lines.Length - start);
        }

        return new Dictionary<string, string?>(StringComparer.Ordinal) { ["rask-plot-text"] = lines.ToString() };
    }

    // With no format, a date reads as the reader's short date, and carries its time only when it has one.
    private static string Moment(DateTime moment) =>
        moment.ToString(moment.TimeOfDay == TimeSpan.Zero ? "d" : "g", CultureInfo.CurrentCulture);

    private static string Number(double value, UiChartFormat? format) =>
        format is null ? value.ToString("#,##0.###", CultureInfo.CurrentCulture) : format.Number(value);
}
