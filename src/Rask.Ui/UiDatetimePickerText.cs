using System.Globalization;

namespace Rask;

/// <summary>
///     How a datetime picker's button writes what is chosen — on the page's first paint, and in the browser after it
///     (<c>rask-calendar.ts</c> writes the same from the patterns the page hands it).
/// </summary>
/// <remarks>
///     Measured on Flux's: one moment is the medium date, the word "at" in grey and the time —
///     <c>Oct 27, 2026 at 10:30 AM</c>. A range drops the year while both ends are in this year
///     (<c>Oct 27, 9:00 AM – Nov 1, 5:00 PM</c>), and one day's range writes the day once and the period once:
///     <c>Oct 22, 2:00 – 4:30 PM</c>.
/// </remarks>
internal static class UiDatetimePickerText
{
    /// <summary>Between a range's two ends.</summary>
    internal const string Separator = " – ";

    /// <summary>Between a range end's date and its time.</summary>
    internal const string Comma = ", ";

    /// <summary>The month and the day without the year, the month abbreviated: "Oct 27".</summary>
    internal static string MonthDayPattern(CultureInfo culture) =>
        culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM", StringComparison.Ordinal);

    /// <summary>The time pattern the picker writes in.</summary>
    internal static string TimePattern(Ui.DatetimePickerTimeFormat format, CultureInfo culture) => format switch
    {
        Ui.DatetimePickerTimeFormat.TwelveHour => "h:mm tt",
        Ui.DatetimePickerTimeFormat.TwentyFourHour => "HH:mm",
        // ICU separates the period with a narrow no-break space; Flux's times have a plain one.
        _ => culture.DateTimeFormat.ShortTimePattern.Replace('\u202F', ' '),
    };

    /// <summary>Whether the pattern tells the time with AM and PM.</summary>
    internal static bool TwelveHour(string pattern) => pattern.Contains('h', StringComparison.Ordinal);

    /// <summary>A time as the list and the button write it.</summary>
    internal static string Time(TimeOnly time, string pattern, CultureInfo culture) =>
        // ICU separates the period with a narrow no-break space; Flux's list has a plain one.
        time.ToString(pattern, culture).Replace(' ', ' ');

    // The word CLDR joins a date and a time with, by the language of the picker's Locale — as Flux's button, which
    // the browser formats in `locale`, writes it — not the app's own language. English for a language not here.
    private static readonly Dictionary<string, string> Joins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "at",
        ["hu"] = "",
    };

    /// <summary>The word between a moment's date and its time in <paramref name="culture" />, spaced as the button writes it.</summary>
    internal static string At(CultureInfo culture)
    {
        var at = Joins.GetValueOrDefault(culture.TwoLetterISOLanguageName, Joins["en"]);
        return at.Length == 0 ? " " : " " + at + " ";
    }

    /// <summary>One moment, as its three pieces: the date, the word between, the time.</summary>
    internal static (string Date, string At, string Time) Moment(DateTime moment, string pattern, CultureInfo culture) =>
        (UiDatePickerText.Medium(DateOnly.FromDateTime(moment), culture), At(culture), Time(TimeOnly.FromDateTime(moment), pattern, culture));

    /// <summary>A range, as what comes before the separator and what comes after it.</summary>
    internal static (string Start, string End) Range(UiDateTimeRange range, string pattern, CultureInfo culture, DateOnly today)
    {
        var (start, end) = (DateOnly.FromDateTime(range.Start), DateOnly.FromDateTime(range.End));
        var (from, to) = (TimeOnly.FromDateTime(range.Start), TimeOnly.FromDateTime(range.End));
        var thisYear = start.Year == today.Year && end.Year == today.Year;
        string Day(DateOnly day) => thisYear ? day.ToString(MonthDayPattern(culture), culture) : UiDatePickerText.Medium(day, culture);

        if (start == end)
        {
            var samePeriod = TwelveHour(pattern) && (from.Hour < 12) == (to.Hour < 12);
            var first = samePeriod ? Time(from, Unperiod(pattern), culture) : Time(from, pattern, culture);
            return (Day(start) + Comma + first, Time(to, pattern, culture));
        }

        return (Day(start) + Comma + Time(from, pattern, culture), Day(end) + Comma + Time(to, pattern, culture));
    }

    /// <summary>The pattern without its period: what a one-day range's start is written in when both ends share it.</summary>
    internal static string Unperiod(string pattern) => pattern.Replace("tt", "", StringComparison.Ordinal).Replace("t", "", StringComparison.Ordinal).Trim();
}
