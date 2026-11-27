using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Postgres;

/// <summary>
///     Writes <c>Min</c> and <c>Max</c> of a <c>uuid</c> — a <see cref="Guid" />, or a typed id over one — in a form
///     PostgreSQL runs: it has no <c>min(uuid)</c> or <c>max(uuid)</c> (42883, still so on 18 and 19).
/// </summary>
/// <remarks>
///     The aggregate runs over the id's text, compared byte by byte (<c>COLLATE "C"</c>), and is cast back:
///     <c>CAST(min(CAST(x AS text) COLLATE "C") AS uuid)</c>. That text is the sixteen bytes in hex, lower case, its
///     hyphens at fixed places, so its byte order is the order PostgreSQL's <c>uuid</c> compares by — and the one .NET's
///     <see cref="Guid.CompareTo(Guid)" /> sorts by, as on SQL Server and SQLite. A plugin's translators are asked first;
///     this one answers only for a <c>uuid</c>, so the provider writes every other aggregate as before.
/// </remarks>
internal sealed class PostgresUuidAggregatePlugin(ISqlExpressionFactory sql, IRelationalTypeMappingSource types)
    : IAggregateMethodCallTranslatorPlugin
{
    public IEnumerable<IAggregateMethodCallTranslator> Translators { get; } = [new Translator(sql, types)];

    private sealed class Translator(ISqlExpressionFactory sql, IRelationalTypeMappingSource types) : IAggregateMethodCallTranslator
    {
        public SqlExpression? Translate(
            MethodInfo method,
            EnumerableExpression source,
            IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (method.DeclaringType != typeof(Queryable)
                || method.Name is not (nameof(Queryable.Min) or nameof(Queryable.Max))
                || source.Selector is not SqlExpression { TypeMapping: { StoreType: "uuid" } uuid } selector)
            {
                return null;
            }

            // A filtered aggregate (a GroupBy's Where) reads only the rows its predicate keeps; the rest are NULL to it.
            var value = source.Predicate is null
                ? selector
                : sql.Case([new CaseWhenClause(source.Predicate, selector)], elseResult: null);
            var text = new CollateExpression(sql.Convert(value, typeof(string), types.FindMapping(typeof(string))), "C");
            var aggregate = sql.Function(
                string.Equals(method.Name, nameof(Queryable.Min), StringComparison.Ordinal) ? "min" : "max",
                [text],
                nullable: true,
                argumentsPropagateNullability: [false],
                typeof(string));
            return sql.Convert(aggregate, method.ReturnType, uuid);
        }
    }
}
