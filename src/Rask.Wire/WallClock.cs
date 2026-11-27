namespace Rask;

/// <summary>
///     The one rule that turns a wall-clock time — a time of day in a zone, without its offset — into the instant it
///     names: what <c>Moment.FromWall</c>, a moment made from a wall-clock <see cref="DateTime" />, and a column that keeps
///     moments as UTC all follow, so the same wall time is the same instant everywhere.
/// </summary>
/// <remarks>
///     <para>
///         Most wall times name exactly one instant. Twice a year one does not, and the rule is SQL Server's
///         <c>AT TIME ZONE</c> (and java.time's, and NodaTime's lenient resolver), so a database that converts in SQL
///         agrees with the app:
///     </para>
///     <list type="bullet">
///         <item>
///             A wall time the clock shows TWICE — the hour gained when summer time ends, <c>02:30</c> on the last Sunday
///             of October in Budapest — is its FIRST occurrence, still in summer time (<c>02:30+02:00</c>, <c>00:30Z</c>):
///             the earlier of the two, so a reading taken then is never later than it was.
///         </item>
///         <item>
///             A wall time the clock SKIPS — the hour lost when summer time begins, <c>02:30</c> on the last Sunday of
///             March — is moved forward by the length of the jump, to <c>03:30+02:00</c> (<c>01:30Z</c>): the offset in
///             force before the jump, which is where a clock that was not moved forward would have been.
///         </item>
///     </list>
/// </remarks>
internal static class WallClock
{
    /// <summary>The UTC instant <paramref name="wall" /> names on the clocks of <paramref name="zone" />.</summary>
    /// <param name="wall">
    ///     The wall-clock time. One of <see cref="DateTimeKind.Utc" /> is the instant already, and one of
    ///     <see cref="DateTimeKind.Local" /> is converted from the machine's zone: neither is a wall-clock time of
    ///     <paramref name="zone" />, and neither is read as one.
    /// </param>
    /// <param name="zone">The zone whose clocks show it.</param>
    /// <returns>The instant, of <see cref="DateTimeKind.Utc" />.</returns>
    internal static DateTime ToUtc(DateTime wall, TimeZoneInfo zone)
    {
        switch (wall.Kind)
        {
            case DateTimeKind.Utc:
                return wall;
            case DateTimeKind.Local:
                return wall.ToUniversalTime();
        }

        if (zone.IsAmbiguousTime(wall))
        {
            // The larger offset is the summer one, in force until the clocks go back: the first occurrence.
            var first = zone.GetAmbiguousTimeOffsets(wall).Max();
            return DateTime.SpecifyKind(wall - first, DateTimeKind.Utc);
        }

        if (zone.IsInvalidTime(wall))
        {
            // No skipped stretch of a zone in use is longer than a few hours: the offset three hours before is the old one.
            var before = zone.GetUtcOffset(wall.AddHours(-3));
            return DateTime.SpecifyKind(wall - before, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(wall, zone);
    }

    /// <summary>The wall-clock time <paramref name="instant" /> shows on the clocks of <paramref name="zone" />.</summary>
    /// <param name="instant">The instant; one of <see cref="DateTimeKind.Unspecified" /> is read as UTC.</param>
    /// <param name="zone">The zone whose clocks show it.</param>
    /// <returns>The wall-clock time, of <see cref="DateTimeKind.Unspecified" />.</returns>
    internal static DateTime ToWall(DateTime instant, TimeZoneInfo zone)
    {
        var utc = instant.Kind switch
        {
            DateTimeKind.Local => instant.ToUniversalTime(),
            _ => DateTime.SpecifyKind(instant, DateTimeKind.Utc),
        };

        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, zone), DateTimeKind.Unspecified);
    }
}
