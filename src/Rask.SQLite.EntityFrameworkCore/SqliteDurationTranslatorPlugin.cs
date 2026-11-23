using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
///     Writes <c>end - start</c> as the whole microseconds between the two, from <c>unixepoch(x, 'subsec')</c>, and its
///     <c>TotalHours</c> and the rest as that count divided down.
/// </summary>
/// <remarks>
///     <para>
///         <c>CAST(round((unixepoch(end, 'subsec') - unixepoch(start, 'subsec')) * 1000000.0) AS INTEGER)</c>. EF Core
///         stores a date as text, and SQLite's date functions read its first three fractional digits only, so the
///         length is exact to the <b>millisecond</b>; <c>round</c> keeps a whole number of milliseconds from coming back
///         a microsecond short through the floating-point difference.
///     </para>
///     <para>
///         A <see cref="DateTimeOffset" /> is stored with its offset (<c>+02:00</c>), which <c>unixepoch</c> reads, so two
///         values in different offsets are compared in UTC, as their subtraction is.
///     </para>
/// </remarks>
internal sealed class SqliteDurationTranslatorPlugin : IMethodCallTranslatorPlugin, IMemberTranslatorPlugin
{
    private readonly ISqlExpressionFactory _sql;
    private readonly RelationalTypeMapping _double;
    private readonly DurationTranslation _translation;

    /// <summary>Creates the plugin. Resolved by Entity Framework Core.</summary>
    /// <param name="sql">The provider's SQL expression factory.</param>
    /// <param name="typeMappings">The provider's type mappings.</param>
    public SqliteDurationTranslatorPlugin(ISqlExpressionFactory sql, IRelationalTypeMappingSource typeMappings)
    {
        _sql = sql;
        _double = typeMappings.FindMapping(typeof(double))!;
        _translation = new DurationTranslation(sql, typeMappings, Microseconds);
    }

    public IEnumerable<IMethodCallTranslator> Translators => [_translation];

    IEnumerable<IMemberTranslator> IMemberTranslatorPlugin.Translators => [_translation];

    private SqlExpression Microseconds(SqlExpression end, SqlExpression start, Type type, RelationalTypeMapping mapping)
    {
        var seconds = _sql.Subtract(Epoch(end), Epoch(start));
        var microseconds = _sql.Function(
            "round",
            [_sql.Multiply(seconds, _sql.Constant(1e6, _double))],
            nullable: true,
            argumentsPropagateNullability: [true],
            typeof(double),
            _double);
        return _sql.Convert(microseconds, type, mapping);
    }

    private SqlExpression Epoch(SqlExpression date) =>
        _sql.Function(
            "unixepoch",
            [date, _sql.Constant("subsec")],
            nullable: true,
            argumentsPropagateNullability: [true, false],
            typeof(double),
            _double);
}
