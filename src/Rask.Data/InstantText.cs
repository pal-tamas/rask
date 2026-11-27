using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.Data;

/// <summary>
///     A moment inside a JSON document on SQL Server and SQLite, as text that keeps its Z — <c>"2026-10-25T00:30:00Z"</c> —
///     where the provider's own JSON writer of a <see cref="DateTime" /> drops it (SQLite writes
///     <c>"2026-10-25 00:30:00"</c>), and so would leave the instant unreadable from the wall-clock time a document held
///     before.
/// </summary>
/// <remarks>
///     Read back, a value with its Z (or an offset) is the instant, of <see cref="DateTimeKind.Utc" />; one without — a
///     wall-clock time written before a document held instants, in either provider's form — is of
///     <see cref="DateTimeKind.Unspecified" />, for the moment to read as a wall-clock time of <see cref="Clock.Zone" />.
/// </remarks>
internal static class InstantText
{
    /// <summary>The converter, the instant to its text and the text back.</summary>
    public static ValueConverter<DateTime, string> Converter { get; } = new(instant => Write(instant), text => Read(text));

    /// <summary>The instant as ISO 8601 with its Z, to the tick it holds.</summary>
    /// <param name="instant">The instant; one of <see cref="DateTimeKind.Unspecified" /> is read as UTC.</param>
    /// <returns>The text.</returns>
    public static string Write(DateTime instant) =>
        DateTime.SpecifyKind(instant.Kind == DateTimeKind.Local ? instant.ToUniversalTime() : instant, DateTimeKind.Utc)
            .ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture);

    /// <summary>What a document holds, as the instant it names or the wall-clock time it is.</summary>
    /// <param name="text">The text: an instant with its Z or an offset, or a wall-clock time without.</param>
    /// <returns>The instant, of <see cref="DateTimeKind.Utc" />, or the wall-clock time, of <see cref="DateTimeKind.Unspecified" />.</returns>
    public static DateTime Read(string text)
    {
        var read = DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return read.Kind == DateTimeKind.Local
            ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture).UtcDateTime
            : read;
    }
}
