using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Rask.Data;

namespace Rask.Postgres;

/// <summary>
///     What a query on one model needs to know about Rask's nondeterministic collations — <c>rask_text</c>, which
///     <c>OrderTextBy</c> gives every text column, and <c>rask_search</c>, which <c>x.Loosely()</c> asks for — to write
///     the string operations PostgreSQL refuses on them in a form it runs.
/// </summary>
/// <remarks>
///     A nondeterministic collation may call strings of different bytes equal, so PostgreSQL refuses every substring
///     search by one — <c>strpos</c>, <c>replace</c>, the regular expressions, <c>ILIKE</c> — and runs <c>LIKE</c> by it
///     from 18 on. Each such collation has a deterministic twin of the same language, imported by <c>initdb</c> on every
///     server built with ICU (which the nondeterministic one needs anyway), that those functions are written by instead.
/// </remarks>
internal sealed class PostgresNondeterministicText
{
    private readonly IRelationalModel _tables;
    private readonly string? _textTwin;
    private readonly bool _textIgnoresCase;

    private PostgresNondeterministicText(IRelationalModel tables, string? textTwin, bool textIgnoresCase)
    {
        _tables = tables;
        _textTwin = textTwin;
        _textIgnoresCase = textIgnoresCase;
    }

    /// <summary>
    ///     What <paramref name="model" /> says: whether its text columns are <c>rask_text</c>, in which language, and which
    ///     columns name a collation of their own.
    /// </summary>
    /// <param name="model">The design-time model: the read-optimized one a query runs by keeps no column's collation.</param>
    internal static PostgresNondeterministicText Of(IModel model)
    {
        var tables = model.GetRelationalModel();
        if (model.FindAnnotation(TextCollation.Annotation)?.Value is string name
            && TextCollation.Parse(name) is { NondeterministicIcuLocale: not null } collation)
        {
            return new PostgresNondeterministicText(tables, collation.IcuCollation, collation.Options.HasFlag(CompareOptions.IgnoreCase));
        }

        return new PostgresNondeterministicText(tables, textTwin: null, textIgnoresCase: false);
    }

    /// <summary>
    ///     The deterministic collation of the same language as <paramref name="collation" />, or null when that one is
    ///     not a nondeterministic collation of Rask's.
    /// </summary>
    internal string? TwinOf(string? collation) =>
        collation switch
        {
            PostgresSearchCollation.Name => "und-x-icu",
            PostgresTextCollationOptionsExtension.Name => _textTwin,
            _ => null,
        };

    /// <summary>Whether <paramref name="collation" />, one of Rask's nondeterministic ones, compares case aside.</summary>
    internal bool IgnoresCase(string collation) =>
        string.Equals(collation, PostgresSearchCollation.Name, StringComparison.Ordinal) || _textIgnoresCase;

    /// <summary>
    ///     The collation <paramref name="expression" /> is compared by, as PostgreSQL derives it — a <c>COLLATE</c>, else
    ///     its column's, else what it is made of — or null when it has none Rask can tell.
    /// </summary>
    internal string? CollationOf(SqlExpression? expression) =>
        expression switch
        {
            CollateExpression collate => collate.Collation,
            ColumnExpression column => ColumnCollation(column),
            SqlFunctionExpression function => CollationOf(function.Instance) ?? FirstCollation(function.Arguments),
            SqlBinaryExpression binary => CollationOf(binary.Left) ?? CollationOf(binary.Right),
            SqlUnaryExpression unary => CollationOf(unary.Operand),
            CaseExpression @case => FirstCollation(@case.WhenClauses.Select(static w => w.Result)) ?? CollationOf(@case.ElseResult),
            _ => null,
        };

    // A column the query reads from a view or raw SQL carries no table column; the model's text columns are
    // all created with rask_text unless they name their own, so that is what such a text column is compared by.
    private string? ColumnCollation(ColumnExpression column) =>
        column.Column switch
        {
            IColumn { Table: var table } read => _tables.FindTable(table.Name, table.Schema)?.FindColumn(read.Name)?.Collation,
            _ when column.Type == typeof(string) && _textTwin is not null => PostgresTextCollationOptionsExtension.Name,
            _ => null,
        };

    private string? FirstCollation(IEnumerable<SqlExpression>? expressions) =>
        expressions?.Select(CollationOf).FirstOrDefault(static c => c is not null);
}
