// EF1001: AtTimeZoneExpression is the SQL Server provider's own node for AT TIME ZONE, in its Internal namespace; it is
// what EF.Functions.AtTimeZone translates to, and the only way to write the operator into a query.
#pragma warning disable EF1001

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.SqlServer.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Rask.Data;

namespace Rask.SqlServer;

/// <summary>
///     A moment's day on the app's wall calendar in a query — <c>f.FueledAt.Day</c>, which
///     <c>GroupBy(f =&gt; f.FueledAt.Day)</c> reads — off a column that keeps the moment as UTC:
///     <c>CAST(([column] AT TIME ZONE 'UTC') AT TIME ZONE N'Central Europe Standard Time' AS date)</c>.
/// </summary>
/// <remarks>
///     The column is one the model marks <see cref="MomentsInUtc.Annotation" />, and the member <c>Moment.Day</c>, known
///     by name: this package does not reference the one that declares it. The zone is <see cref="Clock.Zone" /> as the
///     query is translated, under SQL Server's name for it (<see cref="SqlServerZone" />); it is in the compiled query's
///     cache key too (<see cref="ZonedQueryCacheKeys{TProvider}" />), so a query run again under another zone is compiled for it.
/// </remarks>
/// <param name="sql">The provider's SQL expression factory.</param>
/// <param name="typeMappings">The provider's type mappings.</param>
internal sealed class SqlServerMomentDayTranslator(ISqlExpressionFactory sql, IRelationalTypeMappingSource typeMappings)
    : IMemberTranslatorPlugin, IMemberTranslator
{
    public IEnumerable<IMemberTranslator> Translators => [this];

    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is not ColumnExpression { Column: { } stored } column ||
            !string.Equals(member.Name, "Day", StringComparison.Ordinal) ||
            !string.Equals(member.DeclaringType?.FullName, "Rask.Values.Moment", StringComparison.Ordinal) ||
            !stored.PropertyMappings.Any(static m => m.Property.FindAnnotation(MomentsInUtc.Annotation)?.Value is true))
        {
            return null;
        }

        var offset = typeMappings.FindMapping(typeof(DateTimeOffset));
        var text = typeMappings.FindMapping(typeof(string));
        var utc = new ColumnExpression(
            column.Name, column.TableAlias, stored, typeof(DateTime), typeMappings.FindMapping(typeof(DateTime), stored.StoreType), column.IsNullable);
        var inUtc = new AtTimeZoneExpression(utc, sql.Constant("UTC", text), typeof(DateTimeOffset), offset);
        var onTheWall = new AtTimeZoneExpression(inUtc, sql.Constant(SqlServerZone.Of(Clock.Zone), text), typeof(DateTimeOffset), offset);
        return sql.Convert(onTheWall, typeof(DateOnly), typeMappings.FindMapping(typeof(DateOnly)));
    }
}
