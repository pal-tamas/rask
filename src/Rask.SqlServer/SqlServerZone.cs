namespace Rask.SqlServer;

/// <summary>
///     A zone under the name SQL Server's <c>AT TIME ZONE</c> reads — Windows's (<c>sys.time_zone_info</c>), on Linux
///     too: <c>Central Europe Standard Time</c> for <c>Europe/Budapest</c>.
/// </summary>
internal static class SqlServerZone
{
    /// <summary>The name of <paramref name="zone" />, or its own id where it has no Windows name.</summary>
    internal static string Of(TimeZoneInfo zone) =>
        zone.HasIanaId && TimeZoneInfo.TryConvertIanaIdToWindowsId(zone.Id, out var windows) ? windows : zone.Id;

    /// <summary>Whether <paramref name="zone" />'s wall clock is UTC itself, so a moment's wall time and its instant are one.</summary>
    internal static bool IsUtc(TimeZoneInfo zone) => zone.BaseUtcOffset == TimeSpan.Zero && !zone.SupportsDaylightSavingTime;
}
