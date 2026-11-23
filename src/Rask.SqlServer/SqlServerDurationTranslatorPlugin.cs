using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Rask.Data;

namespace Rask.SqlServer;

/// <summary>
///     Writes <c>end - start</c> as <c>DATEDIFF_BIG(microsecond, start, end)</c>, and its <c>TotalHours</c> and the rest
///     as that count divided down.
/// </summary>
/// <remarks>
///     Microseconds, not nanoseconds: a <c>bigint</c> of nanoseconds overflows past 292 years, which a sentinel date
///     (<c>DateTime.MinValue</c>, <c>9999-12-31</c>) reaches, and SQL Server then fails the query. Microseconds hold
///     292&#160;000 years, so any two dates fit; the 100&#160;ns below a microsecond are dropped. A
///     <c>datetimeoffset</c> is compared in UTC, as <see cref="DateTimeOffset" /> subtraction is.
/// </remarks>
internal sealed class SqlServerDurationTranslatorPlugin : IMethodCallTranslatorPlugin, IMemberTranslatorPlugin
{
    private readonly ISqlExpressionFactory _sql;
    private readonly DurationTranslation _translation;

    /// <summary>Creates the plugin. Resolved by Entity Framework Core.</summary>
    /// <param name="sql">The provider's SQL expression factory.</param>
    /// <param name="typeMappings">The provider's type mappings.</param>
    public SqlServerDurationTranslatorPlugin(ISqlExpressionFactory sql, IRelationalTypeMappingSource typeMappings)
    {
        _sql = sql;
        _translation = new DurationTranslation(sql, typeMappings, Microseconds);
    }

    public IEnumerable<IMethodCallTranslator> Translators => [_translation];

    IEnumerable<IMemberTranslator> IMemberTranslatorPlugin.Translators => [_translation];

    private SqlExpression Microseconds(SqlExpression end, SqlExpression start, Type type, RelationalTypeMapping mapping) =>
        _sql.Function(
            "DATEDIFF_BIG",
            [_sql.Fragment("microsecond"), start, end],
            nullable: true,
            argumentsPropagateNullability: [false, true, true],
            type,
            mapping);
}
