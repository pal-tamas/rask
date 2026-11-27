using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Rask.Postgres;

/// <summary>
///     Writes the string operations PostgreSQL refuses on a text in one of Rask's nondeterministic collations
///     (<see cref="PostgresNondeterministicText" />) in a form it runs, comparing as SQL Server and SQLite do where it can.
/// </summary>
/// <remarks>
///     <para>
///         <c>Contains</c>, <c>StartsWith</c> and <c>EndsWith</c> of a constant or a parameter are Npgsql's: a <c>LIKE</c>
///         with the value escaped, which PostgreSQL 18 matches by the collation. Of anything else — another column, an
///         expression — Npgsql writes <c>strpos</c>, <c>left</c> and <c>right</c>, so here they are a <c>LIKE</c> too, the
///         value escaped in the SQL: case (and accents, as the collation says) aside, as on SQL Server.
///     </para>
///     <para>
///         <c>IndexOf</c> has no <c>LIKE</c> form, so it is <c>strpos</c> by the language's deterministic collation over
///         <c>lower(…)</c> of both, for a collation that ignores case: case aside, accents counted. <c>Replace</c>, the
///         <see cref="Regex" /> methods and <c>EF.Functions.ILike</c> run by that deterministic collation as they are —
///         <c>Replace</c> replaces exactly what it is given, as on SQLite — and a text they answer is given the column's
///         collation back, so a comparison on it stays the column's.
///     </para>
///     <para>
///         Each of those is handed back to the provider with its text so collated: a plugin's translators are asked
///         first, and this one answers only for a text in such a collation, so the provider writes the rest as before.
///     </para>
/// </remarks>
internal sealed class PostgresTextTranslatorPlugin(ISqlExpressionFactory sql, ICurrentDbContext current) : IMethodCallTranslatorPlugin
{
    public IEnumerable<IMethodCallTranslator> Translators { get; } = [new Translator(sql, current)];

    private sealed class Translator(ISqlExpressionFactory sql, ICurrentDbContext current) : IMethodCallTranslator
    {
        private static readonly MethodInfo Contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
        private static readonly MethodInfo StartsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
        private static readonly MethodInfo EndsWith = typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;
        private static readonly MethodInfo IndexOfText = typeof(string).GetMethod(nameof(string.IndexOf), [typeof(string)])!;
        private static readonly MethodInfo IndexOfChar = typeof(string).GetMethod(nameof(string.IndexOf), [typeof(char)])!;

        private static readonly MethodInfo[] Recollated =
        [
            typeof(string).GetMethod(nameof(string.Replace), [typeof(string), typeof(string)])!,
            typeof(string).GetMethod(nameof(string.Replace), [typeof(char), typeof(char)])!,
        ];

        public SqlExpression? Translate(
            SqlExpression? instance,
            MethodInfo method,
            IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (!Handles(method))
            {
                return null;
            }

            var text = PostgresNondeterministicText.Of(current.Context.GetService<IDesignTimeModel>().Model);
            if (method == Contains || method == StartsWith || method == EndsWith)
            {
                return Like(text, instance, method, arguments[0]);
            }

            if (method == IndexOfText || method == IndexOfChar)
            {
                return IndexOf(text, instance, method, arguments, logger);
            }

            return Recollate(text, instance, method, arguments, logger);
        }

        private static bool Handles(MethodInfo method) =>
            method == Contains || method == StartsWith || method == EndsWith || method == IndexOfText || method == IndexOfChar
            || Recollated.Contains(method)
            || method.DeclaringType == typeof(Regex)
            || (method.DeclaringType == typeof(NpgsqlDbFunctionsExtensions) && string.Equals(method.Name, nameof(NpgsqlDbFunctionsExtensions.ILike), StringComparison.Ordinal));

        // A constant or a parameter is Npgsql's LIKE already, escaped as the value is bound.
        private SqlExpression? Like(PostgresNondeterministicText text, SqlExpression? instance, MethodInfo method, SqlExpression value)
        {
            if (instance is null
                || value is SqlConstantExpression or SqlParameterExpression
                || Nondeterministic(text, instance, value) is not { } collation
                || text.TwinOf(collation) is not { } twin)
            {
                return null;
            }

            var mapping = instance.TypeMapping;
            // Given the column's collation back on the escaped value itself — COLLATE binds tighter than || — so LIKE matches
            // by it, not by the twin the escaping needed.
            var escaped = Collate(Escaped(sql.ApplyTypeMapping(value, mapping), twin), collation);
            var anything = sql.Constant("%", mapping);
            return sql.Like(instance, Pattern(method, escaped, anything), sql.Constant(@"\", mapping));
        }

        private SqlExpression Pattern(MethodInfo method, SqlExpression escaped, SqlExpression anything)
        {
            if (method == StartsWith)
            {
                return sql.Add(escaped, anything);
            }

            return method == EndsWith ? sql.Add(anything, escaped) : sql.Add(sql.Add(anything, escaped), anything);
        }

        // The value's own %, _ and \ match only themselves: replaced by the deterministic twin, which replace() needs.
        private SqlExpression Escaped(SqlExpression value, string twin)
        {
            var escaped = (SqlExpression)Collate(value, twin);
            foreach (var special in (ReadOnlySpan<string>)[@"\", "%", "_"])
            {
                escaped = Function("replace", [escaped, sql.Constant(special, value.TypeMapping), sql.Constant(@"\" + special, value.TypeMapping)], value);
            }

            return escaped;
        }

        private SqlExpression? IndexOf(
            PostgresNondeterministicText text,
            SqlExpression? instance,
            MethodInfo method,
            IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            var value = arguments[0];
            if (instance is null || Nondeterministic(text, instance, value) is not { } collation || text.TwinOf(collation) is not { } twin)
            {
                return null;
            }

            if (!text.IgnoresCase(collation))
            {
                return Provider(Collate(instance, twin), method, [Collate(value, twin)], logger);
            }

            // lower() by the twin: the language's case mapping, and an expression strpos takes.
            return Provider(
                Function("lower", [Collate(instance, twin)], instance), method, [Function("lower", [Collate(value, twin)], value)], logger);
        }

        // The collation of whichever of the two is in one of Rask's nondeterministic collations — the text's, else the value's.
        private static string? Nondeterministic(PostgresNondeterministicText text, SqlExpression instance, SqlExpression value) =>
            text.CollationOf(instance) is { } own && text.TwinOf(own) is not null ? own : text.CollationOf(value);

        private SqlExpression? Recollate(
            PostgresNondeterministicText text,
            SqlExpression? instance,
            MethodInfo method,
            IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            var collation = arguments.Prepend(instance).Select(text.CollationOf).FirstOrDefault(c => text.TwinOf(c) is not null);
            if (collation is null || text.TwinOf(collation) is not { } twin)
            {
                return null;
            }

            var translated = Provider(
                instance is null ? null : Twinned(text, instance, twin),
                method,
                [.. arguments.Select(a => Twinned(text, a, twin))],
                logger);

            // A text answered by the twin compares by it, case and all: given the column's collation back, it does not.
            return translated is { Type: var type } && type == typeof(string) ? Collate(translated, collation) : translated;
        }

        private static CollateExpression Collate(SqlExpression expression, string collation) => new(expression, collation);

        private static SqlExpression Twinned(PostgresNondeterministicText text, SqlExpression expression, string twin) =>
            text.TwinOf(text.CollationOf(expression)) is null ? expression : Collate(expression, twin);

        // Handed back to the provider: this translator is asked again, finds no text in a nondeterministic collation, and
        // lets the provider's own write it.
        private SqlExpression? Provider(
            SqlExpression? instance,
            MethodInfo method,
            IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            var context = current.Context;
            return context.GetService<IMethodCallTranslatorProvider>().Translate(context.Model, instance, method, arguments, logger);
        }

        private SqlExpression Function(string name, SqlExpression[] arguments, SqlExpression like) =>
            sql.Function(
                name,
                arguments,
                nullable: true,
                argumentsPropagateNullability: [.. arguments.Select(static _ => true)],
                like.Type,
                like.TypeMapping);
    }
}
