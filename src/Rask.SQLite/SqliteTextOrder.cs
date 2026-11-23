using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Rask.SQLite;

/// <summary>
///     Registers, on one open connection, the text comparison a model names with <c>OrderTextBy</c>: the
///     <see cref="SqliteCollations.TextOrder" /> collation, and <c>instr</c> and <c>LIKE</c> compared the same way.
/// </summary>
/// <remarks>
///     <para>
///         The collation is what the model's text columns are created with, so <c>=</c>, a unique index, <c>GROUP BY</c>
///         and <c>ORDER BY</c> on them compare as the culture does. A collation does not reach a function, and EF Core
///         writes <c>Contains</c> as <c>instr(…) &gt; 0</c> and <c>StartsWith</c>/<c>EndsWith</c> as <c>LIKE</c> — which
///         SQLite folds for ASCII alone, so <c>'Árvíztűrő' LIKE 'árv%'</c> is false there. Both are replaced on the
///         connection by the culture's, as SQLite lets an application do, so they answer what SQL Server's
///         <c>CHARINDEX</c> and <c>LIKE</c> answer under the same collation.
///     </para>
///     <para>
///         Like every function and collation, these are un-registered when a pooled connection is returned, so this
///         runs on every open.
///     </para>
/// </remarks>
internal static class SqliteTextOrder
{
    /// <summary>Registers the comparison on <paramref name="connection" />.</summary>
    /// <param name="connection">An open connection.</param>
    /// <param name="compare">The culture whose order it is.</param>
    /// <param name="options">What the comparison ignores — <see cref="CompareOptions.Ordinal" /> for a binary one.</param>
    internal static void Apply(SqliteConnection connection, CompareInfo compare, CompareOptions options)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(compare);

        // A collation runs inside SQLite's comparison loop, where a managed exception cannot unwind: total by construction.
        connection.CreateCollation(SqliteCollations.TextOrder, (x, y) => Compare(compare, options, x, y));

        // A function's exception is reported as the statement's error, so these may refuse what SQLite would.
        connection.CreateFunction<string?, string?, long?>(
            "instr", (text, value) => IndexOf(compare, options, text, value), isDeterministic: true);
        connection.CreateFunction<string?, string?, bool?>(
            "like", (pattern, text) => Like(compare, options, text, pattern, escape: null), isDeterministic: true);
        connection.CreateFunction<string?, string?, string?, bool?>(
            "like", (pattern, text, escape) => Like(compare, options, text, pattern, Escape(escape)), isDeterministic: true);
    }

    internal static int Compare(CompareInfo compare, CompareOptions options, string? x, string? y)
    {
        if (x is null)
        {
            return y is null ? 0 : -1;
        }

        return y is null ? 1 : compare.Compare(x, y, options);
    }

    // instr's answer: where the value first starts, counting characters from 1 as SQLite does — 0 when it is not there.
    internal static long? IndexOf(CompareInfo compare, CompareOptions options, string? text, string? value)
    {
        if (text is null || value is null)
        {
            return null;
        }

        var at = compare.IndexOf(text, value, options);
        return at < 0 ? 0 : CharactersBefore(text, at) + 1;
    }

    internal static bool? Like(CompareInfo compare, CompareOptions options, string? text, string? pattern, char? escape) =>
        text is null || pattern is null ? null : SqliteLikePattern.IsMatch(text, pattern, escape, compare, options);

    private static char? Escape(string? escape) =>
        escape switch
        {
            null => null,
            { Length: 1 } => escape[0],
            _ => throw new ArgumentException("ESCAPE expression must be a single character", nameof(escape)),
        };

    private static int CharactersBefore(string text, int at)
    {
        var count = 0;
        for (var i = 0; i < at; i++)
        {
            if (!char.IsLowSurrogate(text[i]))
            {
                count++;
            }
        }

        return count;
    }
}
