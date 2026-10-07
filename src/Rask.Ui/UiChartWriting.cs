using System.Globalization;

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

    // With no format, a date reads as the reader's short date, and carries its time only when it has one.
    private static string Moment(DateTime moment) =>
        moment.ToString(moment.TimeOfDay == TimeSpan.Zero ? "d" : "g", CultureInfo.CurrentCulture);

    private static string Number(double value, UiChartFormat? format) =>
        format is null ? value.ToString("#,##0.###", CultureInfo.CurrentCulture) : format.Number(value);
}
