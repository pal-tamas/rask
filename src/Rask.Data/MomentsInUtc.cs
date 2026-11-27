using System.Text;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Rask.Data;

/// <summary>
///     The rewrite of a column of moments, kept as the app's wall clock until Rask stored them as UTC, to the UTC instant
///     each row names: the step <c>RaskMigrationsModelDiffer</c> adds to the first migration after the change, for every
///     column the model now marks <see cref="Annotation" /> and the snapshot did not.
/// </summary>
/// <remarks>
///     <para>
///         In the migration it is a <see cref="SqlOperation" /> whose SQL is a comment naming the column — what
///         <c>dotnet ef migrations add</c> writes as <c>migrationBuilder.Sql("-- Rask: …")</c>. The provider's Rask migration
///         SQL generator turns it into the <c>UPDATE</c> as the migration applies, with the app's zone (<c>Clock.Zone</c>)
///         as it is then, so the rows are rewritten in the migration's own transaction, once, with its history row.
///     </para>
///     <para>
///         The rule is the one a <c>Moment</c> follows: a wall time the clock showed twice is
///         its first occurrence, one it skipped moves forward — SQL Server's <c>AT TIME ZONE</c> rule, which its rewrite
///         uses.
///     </para>
/// </remarks>
internal static class MomentsInUtc
{
    /// <summary>
    ///     The property annotation that says the column keeps the moment as UTC, set where it once kept the wall clock
    ///     (<c>UtcMomentConvention</c>); the migration snapshot keeps it.
    /// </summary>
    internal const string Annotation = "Rask:MomentInUtc";

    private const string Prefix = "-- Rask: this column's moments, kept as the app's wall clock until now, are rewritten to UTC as the migration applies: ";

    /// <summary>The step that rewrites one column, as it sits in a migration.</summary>
    internal static SqlOperation Rewriting(string table, string? schema, string column) =>
        new() { Sql = Prefix + Quoted(schema ?? "") + "." + Quoted(table) + "." + Quoted(column) };

    /// <summary>The column <paramref name="operation" /> rewrites, or null for any other operation.</summary>
    internal static (string Table, string? Schema, string Column)? Of(MigrationOperation operation)
    {
        if (operation is not SqlOperation { Sql: { } sql } || !sql.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        if (Unquoted(sql[Prefix.Length..]) is not [var schema, var table, var column])
        {
            return null;
        }

        return (table, schema.Length == 0 ? null : schema, column);
    }

    /// <summary>
    ///     <paramref name="operations" />, each rewrite step replaced by the SQL <paramref name="rewrite" /> writes for its
    ///     column — or by nothing, where it writes none.
    /// </summary>
    internal static IReadOnlyList<MigrationOperation> Written(
        IReadOnlyList<MigrationOperation> operations, Func<string, string?, string, string?> rewrite)
    {
        if (!operations.Any(static o => Of(o) is not null))
        {
            return operations;
        }

        var written = new List<MigrationOperation>(operations.Count);
        foreach (var operation in operations)
        {
            if (Of(operation) is not { } column)
            {
                written.Add(operation);
            }
            else if (rewrite(column.Table, column.Schema, column.Column) is { } sql)
            {
                written.Add(new SqlOperation { Sql = sql });
            }
        }

        return written;
    }

    /// <summary>
    ///     The steps a migration from <paramref name="source" /> to <paramref name="target" /> needs: one for each column of
    ///     a table both have, marked in the target and not in the source. A table or column the migration creates holds no
    ///     row to rewrite, and the first migration has no source.
    /// </summary>
    internal static IEnumerable<SqlOperation> Between(IRelationalModel? source, IRelationalModel target)
    {
        if (source is null)
        {
            yield break;
        }

        foreach (var table in target.Tables)
        {
            if (source.FindTable(table.Name, table.Schema) is not { } before)
            {
                continue;
            }

            foreach (var column in table.Columns.Where(c => Marked(c) && before.FindColumn(c.Name) is { } was && !Marked(was)).Select(static c => c.Name))
            {
                yield return Rewriting(table.Name, table.Schema, column);
            }
        }
    }

    // [name], a ] in it doubled: a name of any characters, read back as written.
    private static string Quoted(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private static List<string> Unquoted(string text)
    {
        var names = new List<string>();
        var at = 0;
        while (at < text.Length && text[at] == '[')
        {
            var name = new StringBuilder();
            at++;
            while (at < text.Length && (text[at] != ']' || (at + 1 < text.Length && text[at + 1] == ']')))
            {
                at += text[at] == ']' ? 2 : 1;
                name.Append(text[at - 1]);
            }

            names.Add(name.ToString());
            at += 2; // the closing ] and the dot after it
        }

        return names;
    }

    private static bool Marked(IColumn column) =>
        column.PropertyMappings.Any(static m => m.Property.FindAnnotation(Annotation)?.Value is true);
}
