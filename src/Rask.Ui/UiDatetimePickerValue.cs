using System.Globalization;

namespace Rask;

/// <summary>
///     A datetime picker's value as its one bound field carries it between the page and the browser
///     (<c>rask-calendar.ts</c>): <c>yyyy-MM-ddTHH:mm</c> — what a <c>datetime-local</c> field holds — a range as
///     <c>start/end</c>, and nothing for none. Wall-clock times: nothing is converted between time zones.
/// </summary>
internal static class UiDatetimePickerValue
{
    private const string Iso = "yyyy-MM-dd'T'HH:mm";

    internal static string Moment(DateTime moment) => moment == default ? string.Empty : moment.ToString(Iso, CultureInfo.InvariantCulture);

    internal static string Range(UiDateTimeRange range) => range == default ? string.Empty : Moment(range.Start) + "/" + Moment(range.End);

    /// <summary>A moment, or <c>default</c> for nothing; false for text that is no moment.</summary>
    internal static bool TryMoment(string text, out DateTime moment)
    {
        moment = default;
        return string.IsNullOrWhiteSpace(text) || UiDateTimeRange.TryMoment(text.Trim(), out moment);
    }

    /// <summary>A range, or <c>default</c> for nothing; false for text that is no range or one that ends before it starts.</summary>
    internal static bool TryRange(string text, out UiDateTimeRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var parts = text.Split('/');
        if (parts.Length != 2 || !UiDateTimeRange.TryMoment(parts[0].Trim(), out var start) || !UiDateTimeRange.TryMoment(parts[1].Trim(), out var end) || end <= start)
        {
            return false;
        }

        range = new UiDateTimeRange(start, end);
        return true;
    }

    /// <summary>Minutes since midnight, as the browser counts a time.</summary>
    internal static int Minutes(TimeOnly time) => (time.Hour * 60) + time.Minute;

    /// <summary>A moment without its seconds: the picker is precise to the minute.</summary>
    internal static DateTime ToMinute(DateTime moment) => new(moment.Year, moment.Month, moment.Day, moment.Hour, moment.Minute, 0, moment.Kind);
}
