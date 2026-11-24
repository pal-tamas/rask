using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     What an <c>ExecuteDelete</c> statement EF Core wrote was deleting from: the table, the name its columns are
///     qualified by, and the <c>FROM … WHERE …</c> that picks the rows.
/// </summary>
/// <remarks>
///     EF Core writes the table it deletes from with an alias — <c>DELETE FROM "Trailer" AS "t" WHERE …</c> on SQLite
///     and PostgreSQL, <c>SET NOCOUNT OFF; DELETE FROM [t] FROM [Trailer] AS [t] WHERE …</c> on SQL Server — so the first
///     <c>FROM table AS alias</c> is the one; a subquery's comes after it.
/// </remarks>
/// <param name="Schema">The table's schema, unquoted; null when the statement names none.</param>
/// <param name="Table">The table, unquoted.</param>
/// <param name="Qualifier">What the statement qualifies the table's columns with, quoted as written.</param>
/// <param name="From">The statement from its <c>FROM</c> on.</param>
internal sealed record BulkDelete(string? Schema, string Table, string Qualifier, string From)
{
    private const string FromWord = "FROM ";
    private const string AsWord = "AS ";
    private const string NoCountOff = "SET NOCOUNT OFF;";

    /// <summary>The delete <paramref name="sql" /> is, or <see langword="null" /> when it is not one EF Core wrote.</summary>
    internal static BulkDelete? Parse(string sql)
    {
        sql = WithoutRowCountSetting(sql);
        if (!sql.StartsWith("DELETE ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        for (var at = sql.IndexOf(FromWord, StringComparison.OrdinalIgnoreCase);
             at >= 0;
             at = sql.IndexOf(FromWord, at + FromWord.Length, StringComparison.OrdinalIgnoreCase))
        {
            var cursor = at + FromWord.Length;
            if (Identifier(sql, ref cursor) is not { Count: > 0 } table)
            {
                continue;
            }

            var afterTable = cursor;
            SkipSpace(sql, ref cursor);
            if (Starts(sql, cursor, AsWord))
            {
                cursor += AsWord.Length;
                SkipSpace(sql, ref cursor);
                var aliasAt = cursor;
                if (Identifier(sql, ref cursor) is [_])
                {
                    return Of(table, sql[aliasAt..cursor], sql[at..]);
                }
            }
            else if (sql[..at].Trim().Equals("DELETE", StringComparison.OrdinalIgnoreCase) && !Starts(sql, cursor, FromWord))
            {
                // A statement with no alias qualifies the columns with the table itself — but SQL Server's
                // `DELETE FROM [t] FROM …` names only the alias here.
                return Of(table, sql[(at + FromWord.Length)..afterTable], sql[at..]);
            }
        }

        return null;
    }

    /// <summary>The entity types mapped to the table, in <paramref name="model" />.</summary>
    internal List<IEntityType> TypesIn(IModel model) =>
        [.. model.GetEntityTypes().Where(type =>
            string.Equals(type.GetTableName(), Table, StringComparison.Ordinal) &&
            (Schema is null || string.Equals(type.GetSchema(), Schema, StringComparison.Ordinal)))];

    private static BulkDelete Of(List<string> table, string qualifier, string from) =>
        new(table.Count > 1 ? table[^2] : null, table[^1], qualifier, from.TrimEnd().TrimEnd(';'));

    // SQL Server's provider opens the statement with `SET NOCOUNT OFF;`, so the row count comes back: the delete is
    // what follows it.
    private static string WithoutRowCountSetting(string sql)
    {
        var statement = sql.TrimStart();
        return statement.StartsWith(NoCountOff, StringComparison.OrdinalIgnoreCase)
            ? statement[NoCountOff.Length..].TrimStart()
            : statement;
    }

    private static bool Starts(string sql, int at, string word) =>
        string.Compare(sql, at, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0;

    // "a"."b", [a].[b], `a`, or a bare word: its parts, unquoted; the cursor left after it.
    private static List<string>? Identifier(string sql, ref int cursor)
    {
        var parts = new List<string>();
        while (cursor < sql.Length)
        {
            if (Part(sql, ref cursor) is not { } part)
            {
                return null;
            }

            parts.Add(part);
            if (cursor < sql.Length && sql[cursor] == '.')
            {
                cursor++;
                continue;
            }

            break;
        }

        return parts.Count == 0 ? null : parts;
    }

    private static string? Part(string sql, ref int cursor)
    {
        var open = sql[cursor];
        var close = open switch
        {
            '"' => '"',
            '[' => ']',
            '`' => '`',
            _ => '\0',
        };

        if (close == '\0')
        {
            var start = cursor;
            while (cursor < sql.Length && (char.IsLetterOrDigit(sql[cursor]) || sql[cursor] == '_'))
            {
                cursor++;
            }

            return cursor > start ? sql[start..cursor] : null;
        }

        var end = sql.IndexOf(close, cursor + 1);
        if (end < 0)
        {
            return null;
        }

        var name = sql[(cursor + 1)..end];
        cursor = end + 1;
        return name;
    }

    private static void SkipSpace(string sql, ref int cursor)
    {
        while (cursor < sql.Length && char.IsWhiteSpace(sql[cursor]))
        {
            cursor++;
        }
    }
}
