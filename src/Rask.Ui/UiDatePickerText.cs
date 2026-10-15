using System.Globalization;

namespace Rask;

/// <summary>How a date picker's button writes what is chosen — on the page's first paint, and in the browser after it.</summary>
internal static class UiDatePickerText
{
    /// <summary>Between a range's two days.</summary>
    internal const string Separator = " – ";

    /// <summary>The locale's long date without its weekday, the month abbreviated: "Jan 20, 2026".</summary>
    internal static string MediumPattern(CultureInfo culture) =>
        culture.DateTimeFormat.LongDatePattern
            .Replace("dddd, ", "", StringComparison.Ordinal)
            .Replace(", dddd", "", StringComparison.Ordinal)
            .Replace("dddd", "", StringComparison.Ordinal)
            .Replace("MMMM", "MMM", StringComparison.Ordinal)
            .Trim();

    /// <summary>A day as the button shows it.</summary>
    internal static string Medium(DateOnly date, CultureInfo culture) => date.ToString(MediumPattern(culture), culture);
}
