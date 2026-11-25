using System.Globalization;

namespace Rask.Values;

/// <summary>
///     A moment or a day written as text, read onto the app's wall clock (<see cref="Clock.Zone" />): what a value object's
///     generated <c>Parse</c> and <c>TryParse</c> read.
/// </summary>
/// <remarks>
///     <para>
///         Culture-invariant, so a file or an API reads the same on every machine. Without a format it is ISO 8601 and
///         the shapes the invariant culture reads (<c>2026-11-23T08:35:51.000+01:00</c>, <c>…Z</c>,
///         <c>2026-11-23 08:35</c>, <c>2026-11-23</c>); with one, exactly that format (<c>yyyy.MM.dd. HH:mm</c>).
///     </para>
///     <para>
///         A moment that carries its offset (<c>Z</c>, <c>+01:00</c>) is converted to the wall clock of
///         <see cref="Clock.Zone" />; one without is the wall clock's already, as it is written. The answer is always of
///         <see cref="DateTimeKind.Unspecified" />.
///     </para>
/// </remarks>
internal static class WallText
{
    private static CultureInfo Invariant => CultureInfo.InvariantCulture;

    /// <summary>Reads <paramref name="text" /> as a moment on the wall clock; false when it is none.</summary>
    internal static bool TryMoment(string? text, string? format, out DateTime moment)
    {
        moment = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        // RoundtripKind tells whether the text carries an offset: Utc for "Z", Local for "+01:00", Unspecified for none.
        // An offset is read again as one, so the moment is converted from it and never through the machine's own zone.
        if (!Read(trimmed, format, out var read))
        {
            return false;
        }

        if (read.Kind is DateTimeKind.Unspecified)
        {
            moment = read;
            return true;
        }

        if (!ReadOffset(trimmed, format, out var offset))
        {
            return false;
        }

        moment = DateTime.SpecifyKind(Clock.WallAt(offset), DateTimeKind.Unspecified);
        return true;
    }

    /// <summary>Reads <paramref name="text" /> as a day on the wall calendar — a moment's day, for a moment; false when it is none.</summary>
    internal static bool TryDay(string? text, string? format, out DateOnly day)
    {
        var read = TryMoment(text, format, out var moment);
        day = read ? DateOnly.FromDateTime(moment) : default;
        return read;
    }

    /// <summary>The words a text that is no moment (or no day) is refused with.</summary>
    internal static FormatException NotA(string what, string? text, string? format) =>
        new(format is null
            ? $"'{text}' is not a {what}: write it as ISO 8601 (2026-11-23T08:35:00, with Z or an offset for a moment elsewhere), or name its format."
            : $"'{text}' is not a {what} in the format '{format}'.");

    private static bool Read(string text, string? format, out DateTime read) =>
        format is null
            ? DateTime.TryParse(text, Invariant, DateTimeStyles.RoundtripKind, out read)
            : DateTime.TryParseExact(text, format, Invariant, DateTimeStyles.RoundtripKind, out read);

    private static bool ReadOffset(string text, string? format, out DateTimeOffset read) =>
        format is null
            ? DateTimeOffset.TryParse(text, Invariant, DateTimeStyles.None, out read)
            : DateTimeOffset.TryParseExact(text, format, Invariant, DateTimeStyles.None, out read);
}
