using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     The half of <see cref="QueryDuration" /> every provider shares: the type a length of time is written under, and
///     its <c>TotalSeconds</c>, <c>TotalHours</c> and the rest. A provider says only how it counts the microseconds.
/// </summary>
/// <remarks>Source-linked into <c>Rask.SqlServer</c> beside <see cref="QueryDuration" />.</remarks>
/// <param name="sql">The provider's SQL expression factory.</param>
/// <param name="typeMappings">The provider's type mappings.</param>
/// <param name="microseconds">
///     The provider's SQL for the whole microseconds from the start (the second argument) to the end (the first), typed
///     as the third and mapped as the fourth — or null when it cannot write it for these arguments.
/// </param>
internal sealed class DurationTranslation(
    ISqlExpressionFactory sql,
    IRelationalTypeMappingSource typeMappings,
    Func<SqlExpression, SqlExpression, Type, RelationalTypeMapping, SqlExpression?> microseconds)
    : IMethodCallTranslator, IMemberTranslator
{
    private readonly RelationalTypeMapping _duration = (RelationalTypeMapping)typeMappings.FindMapping(typeof(long))!
        .WithComposedConverter(new QueryDuration.MicrosecondsConverter());

    private readonly RelationalTypeMapping _double = typeMappings.FindMapping(typeof(double))!;

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (!QueryDuration.IsBetween(method))
        {
            return null;
        }

        var end = sql.ApplyDefaultTypeMapping(arguments[0]);
        var start = sql.ApplyDefaultTypeMapping(arguments[1]);

        // A column stored through a converter of the app's own (ticks, a Unix time) is not the date the SQL reads, so
        // it is left untranslated — EF Core then says so — rather than computed wrong.
        return end.TypeMapping?.Converter is null && start.TypeMapping?.Converter is null
            ? microseconds(end, start, method.ReturnType, _duration)
            : null;
    }

    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance?.TypeMapping?.Converter is not QueryDuration.MicrosecondsConverter
            || QueryDuration.MicrosecondsPer(member) is not { } factor)
        {
            return null;
        }

        return sql.Divide(sql.Convert(instance, typeof(double), _double), sql.Constant(factor, _double));
    }
}
