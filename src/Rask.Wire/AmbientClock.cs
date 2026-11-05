namespace Rask;

/// <summary>
///     The clock <c>3.Days.Ago</c> and <c>2.Hours.FromNow</c> read: a test's frozen clock when one is in
///     scope, else the app's <see cref="TimeProvider" />, else the system clock.
/// </summary>
/// <remarks>
///     The override is per async flow, so parallel tests that each freeze time never see each other's.
/// </remarks>
internal static class AmbientClock
{
    private static readonly AsyncLocal<TimeProvider?> Override = new();
    private static readonly AsyncLocal<TimeZoneInfo?> ZoneOverride = new();

    /// <summary>The app-wide clock, which a host sets from its registered <see cref="TimeProvider" />.</summary>
    internal static TimeProvider Default { get; set; } = TimeProvider.System;

    /// <summary>The app's wall-clock zone, as its host was told it (<c>AddRask(timeZone: …)</c>); null when it was not.</summary>
    internal static TimeZoneInfo? ConfiguredZone { get; set; }

    internal static TimeProvider Current => Override.Value ?? Default;

    internal static DateTimeOffset Now => Current.GetUtcNow();

    /// <summary>
    ///     The zone the wall clock shows: a test's, else the app's configured one, else the clock's own local zone —
    ///     the machine's (or the browser's), unless a test's <see cref="TimeProvider" /> says another.
    /// </summary>
    internal static TimeZoneInfo Zone => ZoneOverride.Value ?? ConfiguredZone ?? Current.LocalTimeZone;

    /// <summary>Uses <paramref name="clock" /> in this async flow until the returned scope is disposed.</summary>
    internal static IDisposable Use(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var previous = Override.Value;
        Override.Value = clock;
        return new Scope(() => Override.Value = previous);
    }

    /// <summary>Shows the wall clock in <paramref name="zone" /> in this async flow until the returned scope is disposed.</summary>
    internal static IDisposable UseZone(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var previous = ZoneOverride.Value;
        ZoneOverride.Value = zone;
        return new Scope(() => ZoneOverride.Value = previous);
    }

    /// <summary>
    ///     Makes <paramref name="timeZone" /> the app's wall-clock zone — what a host does with <c>AddRask(timeZone: …)</c>.
    /// </summary>
    /// <param name="timeZone">An IANA id — <c>"Europe/Budapest"</c> — or a Windows one.</param>
    /// <param name="call">The call that named it, as the app wrote it, for the error.</param>
    /// <exception cref="ArgumentException">No zone by that id is known to this machine.</exception>
    internal static void Configure(string timeZone, string call)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZone);

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone))
        {
            throw new ArgumentException(
                $"{call} names no time zone this machine knows. Name an IANA zone such as \"Europe/Budapest\"; " +
                "a container image needs its tzdata package for one.", nameof(timeZone));
        }

        ConfiguredZone = zone;
    }

    /// <summary>A moment as the wall clock showed it: <paramref name="moment" /> in <see cref="Zone" />, without its offset.</summary>
    internal static DateTime OnTheWall(DateTimeOffset moment) => TimeZoneInfo.ConvertTime(moment, Zone).DateTime;

    /// <summary>
    ///     A <see cref="DateTime" /> as the wall clock shows it: a UTC or machine-local one converted into <see cref="Zone" />,
    ///     and an unspecified one — which is the wall's already — as it is.
    /// </summary>
    internal static DateTime OnTheWall(DateTime moment) =>
        moment.Kind == DateTimeKind.Unspecified ? moment : OnTheWall(new DateTimeOffset(moment));

    private sealed class Scope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
