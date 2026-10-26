using System.Globalization;

namespace Rask;

/// <summary>
///     A datetime picker's times, resolved: which are listed, which cannot be picked on a given day, how one is
///     written, and where the list opens.
/// </summary>
/// <remarks>
///     Flux's rules, measured: <c>MinTime</c> / <c>MaxTime</c> leave a time out of the list on every day;
///     <c>Min</c> / <c>Max</c> rule out the times before or after them on their own day only; an unavailable time is
///     listed and cannot be picked — every day, or the one day an entry names. With no day chosen only the daily
///     entries count.
/// </remarks>
internal sealed record UiDatetimeTimes(
    string Id,
    IReadOnlyList<TimeOnly> Listed,
    string Pattern,
    CultureInfo Culture,
    bool Typed,
    bool Disabled,
    DateTime? Min,
    DateTime? Max,
    IReadOnlyList<UiDatetimeSlot> Unavailable,
    TimeOnly? OpenTo,
    TimeOnly Now)
{
    internal bool Twelve => UiDatetimePickerText.TwelveHour(Pattern);

    /// <summary>A time as the list and the buttons write it.</summary>
    internal string Written(TimeOnly time) => UiDatetimePickerText.Time(time, Pattern, Culture);

    /// <summary>Whether <paramref name="time" /> cannot be picked on <paramref name="day" /> (no day: every day's rules only).</summary>
    internal bool Off(DateOnly? day, TimeOnly time)
    {
        if (day is { } d)
        {
            if (Min is { } min && (d < DateOnly.FromDateTime(min) || (d == DateOnly.FromDateTime(min) && time < TimeOnly.FromDateTime(min))))
            {
                return true;
            }

            if (Max is { } max && (d > DateOnly.FromDateTime(max) || (d == DateOnly.FromDateTime(max) && time > TimeOnly.FromDateTime(max))))
            {
                return true;
            }
        }

        return Unavailable.Any(slot =>
            slot.From is { } from && (slot.Date is null || slot.Date == day) && time >= from && time <= (slot.To ?? from));
    }

    /// <summary>
    ///     The time a list opens on: the chosen one where it is listed, else the last one that can be picked on
    ///     <paramref name="day" /> not after <see cref="OpenTo" />, else not after now — the first that can be picked
    ///     when every one is later.
    /// </summary>
    internal TimeOnly? Start(TimeOnly? chosen, DateOnly? day)
    {
        if (chosen is { } c && Listed.Contains(c))
        {
            return c;
        }

        var open = Listed.Where(time => !Off(day, time)).ToList();
        var at = OpenTo ?? Now;
        var earlier = open.Where(time => time <= at).ToList();
        if (earlier.Count > 0)
        {
            return earlier[^1];
        }

        return open.Count > 0 ? open[0] : null;
    }

    /// <summary>The ids the rows of the list numbered <paramref name="list" /> are named by.</summary>
    internal string ListId(int list) => Id + "-times-" + list.ToString(CultureInfo.InvariantCulture);
}
