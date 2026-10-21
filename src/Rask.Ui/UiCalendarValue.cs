using System.Globalization;

namespace Rask;

/// <summary>
///     A calendar's value as its one bound field carries it between the page and the browser
///     (<c>rask-calendar.ts</c>): <c>yyyy-MM-dd</c> for a day, days joined by commas, a range as <c>start/end</c>
///     with its preset's key after a third slash, and nothing for none.
/// </summary>
internal static class UiCalendarValue
{
    private const string Iso = "yyyy-MM-dd";

    internal static string Day(DateOnly day) => day == default ? string.Empty : day.ToString(Iso, CultureInfo.InvariantCulture);

    internal static string Days(IEnumerable<DateOnly>? days) => days is null ? string.Empty : string.Join(',', days.Order().Select(Day));

    internal static string Range(UiDateRange range)
    {
        if (range == default)
        {
            return string.Empty;
        }

        var preset = range.Preset is { } named ? "/" + UiDateRangePresets.Key(named) : string.Empty;
        return Day(range.Start) + "/" + Day(range.End) + preset;
    }

    /// <summary>A day, or <c>default</c> for nothing.</summary>
    internal static bool TryDay(string text, out DateOnly day)
    {
        day = default;
        return string.IsNullOrWhiteSpace(text) || DateOnly.TryParseExact(text.Trim(), Iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }

    internal static bool TryDays(string text, out List<DateOnly> days)
    {
        days = [];
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryDay(part, out var day))
            {
                return false;
            }

            days.Add(day);
        }

        days.Sort();
        return true;
    }

    internal static bool TryRange(string text, out UiDateRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var parts = text.Split('/');
        if (parts.Length is < 2 or > 3 || !TryDay(parts[0], out var start) || !TryDay(parts[1], out var end) || start == default || end == default)
        {
            return false;
        }

        var preset = parts.Length == 3 ? UiDateRangePresets.FromKey(parts[2]) : null;
        range = UiDateRange.Between(start, end) with { Preset = preset };

        return true;
    }
}
