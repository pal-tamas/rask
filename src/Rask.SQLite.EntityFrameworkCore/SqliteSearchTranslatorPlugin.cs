using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.SQLite;

/// <summary>
///     Writes <c>Contains</c>, <c>StartsWith</c> and <c>EndsWith</c> on a text <c>x.Loosely()</c> compares as
///     <c>rask_search(text, value, how)</c>: SQLite's <c>instr</c> and <c>LIKE</c> see no collation, so the
///     <see cref="SqliteCollations.SearchOrder" /> the text carries would not reach them.
/// </summary>
/// <remarks>
///     A plugin's translators are asked before the provider's own, and this one answers only for a text in that collation:
///     every other <c>Contains</c> is SQLite's as before. <c>==</c> and <c>OrderBy</c> need nothing — SQLite compares
///     by the collation the text is written with.
/// </remarks>
internal sealed class SqliteSearchTranslatorPlugin(ISqlExpressionFactory sql, IRelationalTypeMappingSource typeMappings)
    : IMethodCallTranslatorPlugin
{
    public IEnumerable<IMethodCallTranslator> Translators { get; } = [new Translator(sql, typeMappings.FindMapping(typeof(bool))!)];

    private sealed class Translator(ISqlExpressionFactory sql, RelationalTypeMapping boolean) : IMethodCallTranslator
    {
        private static readonly MethodInfo Contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
        private static readonly MethodInfo StartsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
        private static readonly MethodInfo EndsWith = typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;

        public SqlExpression? Translate(
            SqlExpression? instance,
            MethodInfo method,
            IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (instance is not CollateExpression { Collation: SqliteCollations.SearchOrder } searched)
            {
                return null;
            }

            if (PlaceOf(method) is not { } where)
            {
                return null;
            }

            var text = searched.Operand;
            var value = sql.ApplyTypeMapping(arguments[0], text.TypeMapping);
            return sql.Function(
                SqliteSearchText.Function,
                [text, value, sql.Constant((long)where)],
                nullable: true,
                argumentsPropagateNullability: [true, true, false],
                typeof(bool),
                boolean);
        }

        private static SqliteSearchText.Place? PlaceOf(MethodInfo method)
        {
            if (method == Contains)
            {
                return SqliteSearchText.Place.Anywhere;
            }

            if (method == StartsWith)
            {
                return SqliteSearchText.Place.Start;
            }

            return method == EndsWith ? SqliteSearchText.Place.End : null;
        }
    }
}
