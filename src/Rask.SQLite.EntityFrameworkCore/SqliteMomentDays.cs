using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
///     A UTC moment's day on the app's wall calendar, on SQLite: <c>rask_moment_wall_day(column)</c>
///     (<see cref="SqliteMoments.WallDay" />), which reads <see cref="Clock.Zone" /> as each row is read and writes the
///     day as SQLite keeps a <see cref="DateOnly" />, <c>yyyy-MM-dd</c>.
/// </summary>
/// <param name="sql">The provider's SQL expression factory.</param>
/// <param name="typeMappings">The provider's type mappings.</param>
internal sealed class SqliteMomentDays(ISqlExpressionFactory sql, IRelationalTypeMappingSource typeMappings) : IMomentDays
{
    public SqlExpression WallDay(SqlExpression utc, TimeZoneInfo zone) =>
        sql.Function(SqliteMoments.WallDay, [utc], nullable: true, argumentsPropagateNullability: [true], typeof(DateOnly),
            typeMappings.FindMapping(typeof(DateOnly)));
}
