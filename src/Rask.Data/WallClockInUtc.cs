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
///         A wall time names its instant by the one rule a <c>Moment</c> follows (<see cref="WallClock" />): one the clock
///         skips when summer time begins is moved forward by the jump, and one it shows twice when summer time ends is
///         its first occurrence — as SQL Server's <c>AT TIME ZONE</c> reads them.
///     </para>
/// </remarks>
internal static class WallClockInUtc
{
    /// <summary>The converter, wall clock to UTC and back.</summary>
    public static ValueConverter<DateTime, DateTime> Converter { get; } = new(wall => ToUtc(wall), utc => ToWall(utc));

    /// <summary>The UTC instant a wall-clock time of <see cref="Clock.Zone" /> is, by the rule a <c>Moment</c> follows.</summary>
    /// <param name="wall">The wall-clock time; a UTC one is kept as it is.</param>
    /// <returns>The instant, of <see cref="DateTimeKind.Utc" />.</returns>
    public static DateTime ToUtc(DateTime wall) => WallClock.ToUtc(wall, Clock.Zone);

    /// <summary>The wall-clock time of <see cref="Clock.Zone" /> a UTC instant shows.</summary>
    /// <param name="utc">The instant, as the database hands it back.</param>
    /// <returns>The wall-clock time, of <see cref="DateTimeKind.Unspecified" />.</returns>
    public static DateTime ToWall(DateTime utc) => WallClock.ToWall(utc, Clock.Zone);
}
