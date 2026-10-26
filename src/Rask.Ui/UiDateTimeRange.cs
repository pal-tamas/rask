using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Rask;

/// <summary>
/// A start and an end, each a date with its own time of day — what a range datetime picker binds. Flux UI's
/// <c>DateTimeRange</c>.
/// </summary>
/// <remarks>
/// <para>
/// Wall-clock times, as a <c>datetime-local</c> field holds them: <c>2026-10-14T10:30</c> is half past ten on that
/// day wherever it is read, and nothing converts it between time zones. Precise to the minute: seconds are dropped.
/// </para>
/// <para>
/// "Nothing chosen" is <c>default(UiDateTimeRange)</c> — bind a <c>UiDateTimeRange?</c> where unset and a real range
/// must differ. One value in an address: <c>2026-10-14T22:00..2026-10-15T02:00</c>.
/// </para>
/// </remarks>
/// <param name="Start">When the range begins.</param>
/// <param name="End">When the range ends: after <paramref name="Start" />.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct UiDateTimeRange(DateTime Start, DateTime End) : IParsable<UiDateTimeRange>
{
    private const string Iso = "yyyy-MM-dd'T'HH:mm";

    private const string Through = "..";

    /// <summary>Whether the range has a start — Flux's <c>hasStart()</c>.</summary>
    public bool HasStart => Start != default;

    /// <summary>Whether the range has an end — Flux's <c>hasEnd()</c>.</summary>
    public bool HasEnd => End != default;

    /// <summary>Whether <paramref name="moment" /> falls inside the range, its ends included.</summary>
    public bool Contains(DateTime moment) => this != default && moment >= Start && moment <= End;

    /// <summary>The range as an address carries it: <c>2026-10-14T22:00..2026-10-15T02:00</c>; nothing for an unset range.</summary>
    public override string ToString() =>
        this == default ? string.Empty : Start.ToString(Iso, CultureInfo.InvariantCulture) + Through + End.ToString(Iso, CultureInfo.InvariantCulture);

    /// <summary>Reads a range written as <see cref="ToString" /> writes one.</summary>
    /// <param name="s">The text: <c>2026-10-14T22:00..2026-10-15T02:00</c>.</param>
    /// <param name="provider">Unused: the text is the same in every culture.</param>
    /// <exception cref="FormatException">The text is no range.</exception>
    public static UiDateTimeRange Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var range) ? range : throw new FormatException($"\"{s}\" is no range of times: write 2026-10-14T22:00..2026-10-15T02:00.");

    /// <summary>Reads a range written as <see cref="ToString" /> writes one; false for anything else.</summary>
    /// <param name="s">The text: <c>2026-10-14T22:00..2026-10-15T02:00</c>.</param>
    /// <param name="provider">Unused: the text is the same in every culture.</param>
    /// <param name="result">The range read, or an unset one.</param>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out UiDateTimeRange result)
    {
        result = default;
        var parts = s?.Trim().Split(Through);
        if (parts is not [var start, var end] || !TryMoment(start, out var from) || !TryMoment(end, out var to))
        {
            return false;
        }

        result = new UiDateTimeRange(from, to);
        return true;
    }

    internal static bool TryMoment(string text, out DateTime moment) =>
        DateTime.TryParseExact(text, Iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out moment);
}
