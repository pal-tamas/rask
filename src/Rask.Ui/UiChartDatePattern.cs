using System.Globalization;
using System.Text;

namespace Rask;

/// <summary>
///     The date part of a chart's format, in the order and with the separators the reader's culture writes it:
///     "March 15" in English, "15. März" in German, "március 15." in Hungarian.
/// </summary>
/// <remarks>
///     Built from the culture's own patterns — the month and day, the year and month, the long date, the short
///     date — with each field set to the length asked for. A set of parts no culture pattern covers is written in
///     English order, with the culture's names.
/// </remarks>
internal static class UiChartDatePattern
{
    /// <summary>The culture's pattern for the parts asked for; null when no culture pattern holds exactly them.</summary>
    internal static string? Of(Ui.ChartFormatPart? month, Ui.ChartFormatPart? day, Ui.ChartFormatPart? year, CultureInfo culture)
    {
        var formats = culture.DateTimeFormat;
        if (month is null || (day is null && year is null))
        {
            return null;
        }

        if (month is Ui.ChartFormatPart.Numeric or Ui.ChartFormatPart.TwoDigit)
        {
            return Numeric(month.Value, day, year, formats.ShortDatePattern);
        }

        var source = (day, year) switch
        {
            (not null, not null) => WithoutWeekday(formats.LongDatePattern),
            (not null, null) => formats.MonthDayPattern,
            _ => formats.YearMonthPattern,
        };
        return Reshape(source, month.Value, day, year);
    }

    /// <summary>The culture's long date without its day of the week: Intl's <c>dateStyle: "long"</c>.</summary>
    internal static string WithoutWeekday(string pattern)
    {
        var at = pattern.IndexOf("dddd", StringComparison.Ordinal);
        if (at < 0)
        {
            return pattern;
        }

        var end = at + 4;
        while (end < pattern.Length && pattern[end] is ',' or ' ' or ' ')
        {
            end++;
        }

        var start = at;
        while (end == pattern.Length && start > 0 && pattern[start - 1] is ',' or ' ' or ' ')
        {
            start--;
        }

        return string.Concat(pattern.AsSpan(0, start), pattern.AsSpan(end)).Trim();
    }

    // A numeric month: the parts in the order the culture's short date has them, by its date separator ("/" in a
    // custom pattern is the culture's own).
    private static string Numeric(Ui.ChartFormatPart month, Ui.ChartFormatPart? day, Ui.ChartFormatPart? year, string shortDate)
    {
        var parts = new List<(int At, string Field)> { (shortDate.IndexOf('M', StringComparison.Ordinal), MonthField(month)) };
        if (day is { } d)
        {
            parts.Add((shortDate.IndexOf('d', StringComparison.Ordinal), DayField(d)));
        }

        if (year is { } y)
        {
            parts.Add((shortDate.IndexOf('y', StringComparison.Ordinal), YearField(y)));
        }

        return string.Join('/', parts.OrderBy(part => part.At).Select(part => part.Field));
    }

    // Each field of the culture's pattern at the length asked for; its literals, quoted or not, as they are.
    private static string Reshape(string pattern, Ui.ChartFormatPart month, Ui.ChartFormatPart? day, Ui.ChartFormatPart? year)
    {
        var shaped = new StringBuilder(pattern.Length + 4);
        var at = 0;
        while (at < pattern.Length)
        {
            var c = pattern[at];
            var run = 1;
            if (c is '\'' or '"')
            {
                var close = pattern.IndexOf(c, at + 1);
                run = (close < 0 ? pattern.Length : close + 1) - at;
                shaped.Append(pattern, at, run);
                at += run;
                continue;
            }

            while (at + run < pattern.Length && pattern[at + run] == c)
            {
                run++;
            }

            shaped.Append(c switch
            {
                'M' => MonthField(month),
                'd' when run <= 2 && day is { } d => DayField(d),
                'y' when year is { } y => YearField(y),
                _ => pattern.Substring(at, run),
            });
            at += run;
        }

        return shaped.ToString();
    }

    private static string MonthField(Ui.ChartFormatPart month) => month switch
    {
        Ui.ChartFormatPart.Numeric => "M",
        Ui.ChartFormatPart.TwoDigit => "MM",
        Ui.ChartFormatPart.Long => "MMMM",
        _ => "MMM",
    };

    private static string DayField(Ui.ChartFormatPart day) => day == Ui.ChartFormatPart.TwoDigit ? "dd" : "d";

    private static string YearField(Ui.ChartFormatPart year) => year == Ui.ChartFormatPart.TwoDigit ? "yy" : "yyyy";
}
