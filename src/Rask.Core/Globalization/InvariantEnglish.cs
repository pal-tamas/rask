using System.Globalization;

namespace Rask.Core.Globalization;

/// <summary>
///     US English, for a runtime that carries no culture data: how an English-only browser app writes its numbers,
///     money and dates without shipping ICU (#1305).
/// </summary>
/// <remarks>
///     <para>
///         Such an app is published with <c>InvariantGlobalization</c> (<c>Rask.Wasm.targets</c>), the smallest bundle
///         there is, and every culture in it formats as the invariant one: <c>¤19.99</c>, <c>11/11/2026 15:04</c>,
///         <c>12.50 %</c>. The invariant culture's names of months and days already ARE English; what it lacks is US
///         English's sign, patterns and digits, which are a handful of values rather than a megabyte of ICU. They are
///         set here on a culture named <c>en-US</c> (nameable with no data once <c>PredefinedCulturesOnly</c> is off,
///         as the targets turn it), so a page formats as the same app does on the server.
///     </para>
///     <para>
///         The values are ICU's en-US as .NET reports it, and <c>InvariantEnglishTests</c> holds them to the
///         runtime's own en-US wherever the tests run with ICU, so a change in either shows.
///     </para>
/// </remarks>
internal static class InvariantEnglish
{
    /// <summary>The currency US English writes in its sign.</summary>
    internal const string Currency = "USD";

    private static readonly Lazy<CultureInfo> Built = new(static () => Build(new CultureInfo("en-US", useUserOverride: false)));

    /// <summary>US English, built once.</summary>
    internal static CultureInfo Culture => Built.Value;

    /// <summary>Whether <paramref name="culture" /> is this one — asked without building it.</summary>
    internal static bool Is(CultureInfo culture) => Built.IsValueCreated && ReferenceEquals(culture, Built.Value);

    /// <summary>Whether a culture of <paramref name="name" /> is written as US English: English, or US English.</summary>
    internal static bool Serves(string name) =>
        string.Equals(name, "en", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "en-US", StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes US English's values onto <paramref name="culture" />, read-only after.</summary>
    internal static CultureInfo Build(CultureInfo culture)
    {
        var numbers = culture.NumberFormat;
        numbers.CurrencySymbol = "$";
        numbers.CurrencyPositivePattern = 0; // $n
        numbers.CurrencyNegativePattern = 1; // -$n
        numbers.NumberDecimalDigits = 3;
        numbers.PercentDecimalDigits = 3;
        numbers.PercentPositivePattern = 1; // n%
        numbers.PercentNegativePattern = 1; // -n%
        numbers.PositiveInfinitySymbol = "∞";
        numbers.NegativeInfinitySymbol = "-∞";

        // ICU writes a narrow no-break space before the AM/PM mark.
        const string BeforeMark = "\u202F";
        var dates = culture.DateTimeFormat;
        dates.ShortDatePattern = "M/d/yyyy";
        dates.LongDatePattern = "dddd, MMMM d, yyyy";
        dates.ShortTimePattern = "h:mm" + BeforeMark + "tt";
        dates.LongTimePattern = "h:mm:ss" + BeforeMark + "tt";
        dates.FullDateTimePattern = "dddd, MMMM d, yyyy h:mm:ss" + BeforeMark + "tt";
        dates.MonthDayPattern = "MMMM d";
        dates.YearMonthPattern = "MMMM yyyy";
        dates.AMDesignator = "AM";
        dates.PMDesignator = "PM";
        dates.FirstDayOfWeek = DayOfWeek.Sunday;
        dates.CalendarWeekRule = CalendarWeekRule.FirstDay;

        return CultureInfo.ReadOnly(culture);
    }
}
