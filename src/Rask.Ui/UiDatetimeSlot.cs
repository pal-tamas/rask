using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Rask;

/// <summary>
/// A day, a time of day, or a time on one day — an entry of a datetime picker's <c>Unavailable</c>, and where its
/// <c>OpenTo</c> points. Flux UI writes the same as <c>2026-12-25</c>, <c>12:00-13:00</c> and <c>2026-10-14T15:00-16:00</c>.
/// </summary>
/// <remarks>
/// Each kind converts from the type that says it, so a list reads as Flux's does:
/// <c>[new DateOnly(2026, 12, 25), new UiTimeRange(new(12, 0), new(13, 0)), new DateTime(2026, 10, 14, 10, 0, 0)]</c>.
/// A stretch of times includes both ends, so <c>12:00-13:00</c> blocks 1:00 PM too.
/// </remarks>
/// <param name="Date">The day, or <see langword="null" /> for every day.</param>
/// <param name="From">The first minute, or <see langword="null" /> for the whole day.</param>
/// <param name="To">The last minute: <paramref name="From" /> for a single time.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct UiDatetimeSlot(DateOnly? Date, TimeOnly? From, TimeOnly? To) : IParsable<UiDatetimeSlot>
{
    private const string Day = "yyyy-MM-dd";

    private const string Clock = "HH:mm";

    /// <summary>A stretch of times on one day only: Flux's <c>2026-10-14T15:00-16:00</c>.</summary>
    /// <param name="date">The day.</param>
    /// <param name="from">The first minute.</param>
    /// <param name="to">The last minute.</param>
    public static UiDatetimeSlot On(DateOnly date, TimeOnly from, TimeOnly to) => new(date, from, to);

    /// <summary>A whole day: Flux's <c>2026-12-25</c>.</summary>
    public static UiDatetimeSlot FromDateOnly(DateOnly date) => new(date, null, null);

    /// <summary>One time on every day: Flux's <c>12:00</c>.</summary>
    public static UiDatetimeSlot FromTimeOnly(TimeOnly time) => new(null, time, time);

    /// <summary>A stretch of times on every day: Flux's <c>12:00-13:00</c>.</summary>
    public static UiDatetimeSlot FromUiTimeRange(UiTimeRange times) => new(null, times.Start, times.End);

    /// <summary>One time on one day: Flux's <c>2026-10-14T10:00</c>.</summary>
    public static UiDatetimeSlot FromDateTime(DateTime moment) =>
        new(DateOnly.FromDateTime(moment), Minute(moment), Minute(moment));

    /// <inheritdoc cref="FromDateOnly" />
    public static implicit operator UiDatetimeSlot(DateOnly date) => FromDateOnly(date);

    /// <inheritdoc cref="FromTimeOnly" />
    public static implicit operator UiDatetimeSlot(TimeOnly time) => FromTimeOnly(time);

    /// <inheritdoc cref="FromUiTimeRange" />
    public static implicit operator UiDatetimeSlot(UiTimeRange times) => FromUiTimeRange(times);

    /// <inheritdoc cref="FromDateTime" />
    public static implicit operator UiDatetimeSlot(DateTime moment) => FromDateTime(moment);

    /// <summary>The slot as Flux writes it: <c>2026-12-25</c>, <c>12:00-13:00</c>, <c>2026-10-14T15:00-16:00</c>.</summary>
    public override string ToString()
    {
        var day = Date?.ToString(Day, CultureInfo.InvariantCulture);
        if (From is not { } from)
        {
            return day ?? string.Empty;
        }

        var times = from.ToString(Clock, CultureInfo.InvariantCulture)
                    + (To is { } to && to != from ? "-" + to.ToString(Clock, CultureInfo.InvariantCulture) : string.Empty);
        return day is null ? times : day + "T" + times;
    }

    /// <summary>Reads a slot written as Flux writes one.</summary>
    /// <param name="s">The text: <c>2026-12-25</c>, <c>12:00-13:00</c>, <c>2026-10-14T15:00-16:00</c>.</param>
    /// <param name="provider">Unused: the text is the same in every culture.</param>
    /// <exception cref="FormatException">The text is no slot.</exception>
    public static UiDatetimeSlot Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var slot) ? slot : throw new FormatException($"\"{s}\" is no day or time: write 2026-12-25, 12:00-13:00 or 2026-10-14T15:00-16:00.");

    /// <summary>Reads a slot written as Flux writes one; false for anything else.</summary>
    /// <param name="s">The text: <c>2026-12-25</c>, <c>12:00-13:00</c>, <c>2026-10-14T15:00-16:00</c>.</param>
    /// <param name="provider">Unused: the text is the same in every culture.</param>
    /// <param name="result">The slot read, or an empty one.</param>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out UiDatetimeSlot result)
    {
        result = default;
        var text = s?.Trim() ?? string.Empty;
        DateOnly? date = null;
        if (text.Length >= Day.Length && DateOnly.TryParseExact(text[..Day.Length], Day, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            date = day;
            text = text[Day.Length..];
            if (text.Length == 0)
            {
                result = new UiDatetimeSlot(date, null, null);
                return true;
            }

            if (text[0] != 'T')
            {
                return false;
            }

            text = text[1..];
        }

        var times = text.Split('-');
        if (times.Length is < 1 or > 2 || !TryTime(times[0], out var from))
        {
            return false;
        }

        var to = from;
        if (times.Length == 2 && !TryTime(times[1], out to))
        {
            return false;
        }

        result = new UiDatetimeSlot(date, from, to);
        return true;
    }

    private static bool TryTime(string text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text.Trim(), Clock, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static TimeOnly Minute(DateTime moment) => new(moment.Hour, moment.Minute);
}
