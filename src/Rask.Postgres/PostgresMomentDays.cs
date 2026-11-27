using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Rask.Data;

namespace Rask.Postgres;

/// <summary>
///     A UTC moment's day on the app's wall calendar, on PostgreSQL: <c>CAST(timezone('Europe/Budapest', column) AS date)</c>
///     — <c>timezone(zone, timestamptz)</c> is the wall-clock time in that zone, whatever the session's own.
/// </summary>
/// <remarks>
///     The zone is a constant of the SQL; it is in the compiled query's cache key too
///     (<see cref="ZonedQueryCacheKeys{TProvider}" />), so a query run again under another zone is compiled for it.
/// </remarks>
/// <param name="sql">The provider's SQL expression factory.</param>
/// <param name="typeMappings">The provider's type mappings.</param>
internal sealed class PostgresMomentDays(ISqlExpressionFactory sql, IRelationalTypeMappingSource typeMappings) : IMomentDays
{
    public SqlExpression WallDay(SqlExpression utc, TimeZoneInfo zone)
    {
        var name = !zone.HasIanaId && TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id;
        var wall = sql.Function(
            "timezone",
            [sql.Constant(name, typeMappings.FindMapping(typeof(string))), utc],
            nullable: true,
            argumentsPropagateNullability: [false, true],
            typeof(DateTime),
            typeMappings.FindMapping("timestamp without time zone"));
        return sql.Convert(wall, typeof(DateOnly), typeMappings.FindMapping(typeof(DateOnly)));
    }
}
