using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.Data;

/// <summary>
///     Stores a wall-clock moment — what a <see cref="Rask.Values.Moment" /> holds, in <see cref="Clock.Zone" /> with
///     <see cref="DateTimeKind.Unspecified" /> — as the UTC instant it is, and reads it back onto the wall clock: what a
///     database that keeps a <see cref="DateTime" /> only as UTC (PostgreSQL's <c>timestamptz</c>) is given.
/// </summary>
/// <remarks>
///     <para>
///         The zone is read as each value is converted, so a test's <c>Clock.Fake</c> zone and the app's
///         <c>AddRask(timeZone: …)</c> are both followed.
///     </para>
///     <para>
///         A wall time the clock skips (the hour lost when summer time begins) is read with the offset in force before
///         the jump, so it is stored as the instant just after it; a wall time the clock shows twice (the hour gained in
///         autumn) is stored as its standard-time instant, as <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)" />
///         reads it.
///     </para>
/// </remarks>
internal static class WallClockInUtc
{
    /// <summary>The converter, wall clock to UTC and back.</summary>
    public static ValueConverter<DateTime, DateTime> Converter { get; } = new(wall => ToUtc(wall), utc => ToWall(utc));

    /// <summary>The UTC instant a wall-clock time of <see cref="Clock.Zone" /> is.</summary>
    /// <param name="wall">The wall-clock time; a UTC one is kept as it is.</param>
    /// <returns>The instant, of <see cref="DateTimeKind.Utc" />.</returns>
    public static DateTime ToUtc(DateTime wall)
    {
        if (wall.Kind == DateTimeKind.Utc)
        {
            return wall;
        }

        var zone = Clock.Zone;
        var unspecified = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (!zone.IsInvalidTime(unspecified))
        {
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
        }

        // No skipped stretch of a zone in use is longer than a few hours: the offset three hours before is the old one.
        var before = zone.GetUtcOffset(unspecified.AddHours(-3));
        return DateTime.SpecifyKind(unspecified - before, DateTimeKind.Utc);
    }

    /// <summary>The wall-clock time of <see cref="Clock.Zone" /> a UTC instant shows.</summary>
    /// <param name="utc">The instant, as the database hands it back.</param>
    /// <returns>The wall-clock time, of <see cref="DateTimeKind.Unspecified" />.</returns>
    public static DateTime ToWall(DateTime utc) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Clock.Zone), DateTimeKind.Unspecified);
}
