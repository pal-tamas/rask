using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     The one batch of T-SQL that rewrites every <c>uniqueidentifier</c> column of a model from one layout to the other
///     (<see cref="SqlServerGuidOrder" />), with the foreign keys between them switched off while it runs.
/// </summary>
/// <remarks>
///     <para>
///         The foreign keys are read from <c>sys.foreign_keys</c> as the batch runs, not from the model: one the model
///         does not know (another tool's) is switched off too, and one that was off already stays off. Each is switched
///         back on <c>WITH CHECK</c>, so a key from a table the model does not map — whose column was not rewritten —
///         stops the migration instead of being left pointing nowhere.
///     </para>
///     <para>
///         A table excluded from the context's migrations is another context's to rewrite, and a computed column is the
///         database's: both are left alone.
///     </para>
/// </remarks>
internal static class GuidOrderRewrite
{
    /// <summary>The first line of the rewrite: an exclusive migration, which no older version of the app may run beside.</summary>
    internal const string Marker =
        ExclusiveMigrations.Prefix + ": rewrites every uniqueidentifier between SQL Server's native layout and Rask's time-ordered one";

    private const string UniqueIdentifier = "uniqueidentifier";

    /// <summary>
    ///     The rewrite of <paramref name="model" />'s columns by <paramref name="rewrite" />, leaving out the tables in
    ///     <paramref name="skipped" />; null when there is no column to rewrite.
    /// </summary>
    internal static SqlOperation? Of(
        IRelationalModel model,
        IReadOnlySet<(string Name, string? Schema)> skipped,
        Func<string, string> rewrite,
        ISqlGenerationHelper sql)
    {
        var updates = new StringBuilder();
        var tables = new List<string>();
        foreach (var table in model.Tables.OrderBy(static t => t.Schema, StringComparer.Ordinal).ThenBy(static t => t.Name, StringComparer.Ordinal))
        {
            if (table.IsExcludedFromMigrations || skipped.Contains((table.Name, table.Schema)))
            {
                continue;
            }

            var columns = table.Columns
                .Where(static c => c.ComputedColumnSql is null &&
                                   string.Equals(c.StoreType, UniqueIdentifier, StringComparison.OrdinalIgnoreCase))
                .Select(c => sql.DelimitIdentifier(c.Name))
                .ToList();
            if (columns.Count == 0)
            {
                continue;
            }

            var name = sql.DelimitIdentifier(table.Name, table.Schema);
            tables.Add(name);
            updates.Append("UPDATE ").Append(name).Append(" SET ")
                .AppendJoin(", ", columns.Select(c => c + " = " + rewrite(c)))
                .AppendLine(";");
        }

        if (tables.Count == 0)
        {
            return null;
        }

        var objects = string.Join(", ", tables.Select(static t => "OBJECT_ID(N'" + t.Replace("'", "''", StringComparison.Ordinal) + "')"));
        var script = new StringBuilder()
            .AppendLine(Marker)
            .AppendLine("DECLARE @rask_keys TABLE ([Owner] nvarchar(300) NOT NULL, [Key] nvarchar(300) NOT NULL);")
            .AppendLine("INSERT INTO @rask_keys ([Owner], [Key])")
            .AppendLine("SELECT QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id)), QUOTENAME(fk.name)")
            .AppendLine("FROM sys.foreign_keys AS fk")
            .Append("WHERE fk.is_disabled = 0 AND (fk.parent_object_id IN (").Append(objects)
            .Append(") OR fk.referenced_object_id IN (").Append(objects).AppendLine("));")
            .AppendLine("DECLARE @rask_sql nvarchar(max);")
            .AppendLine(Each("NOCHECK CONSTRAINT"))
            .Append(updates)
            .AppendLine(Each("WITH CHECK CHECK CONSTRAINT"));
        return new SqlOperation { Sql = script.ToString() };
    }

    private static string Each(string alter) =>
        "SELECT @rask_sql = STRING_AGG(CAST(N'ALTER TABLE ' + [Owner] + N' " + alter + " ' + [Key] AS nvarchar(max)), N'; ') FROM @rask_keys;\n"
        + "IF @rask_sql IS NOT NULL EXEC sp_executesql @rask_sql;";
}
