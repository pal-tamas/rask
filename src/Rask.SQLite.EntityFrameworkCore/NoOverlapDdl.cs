using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
///     Writes the SQLite triggers holding each <c>b.NoOverlap(…)</c> a model declares: an <c>AFTER INSERT</c> and an
///     <c>AFTER UPDATE</c> trigger per rule that <c>RAISE(ABORT)</c> when the row overlaps another, plus an index over the
///     key and the start.
/// </summary>
/// <remarks>
///     <para>
///         SQLite has no exclusion constraint, so the triggers ARE the rule — and they ask what PostgreSQL's exclusion
///         constraint asks: a key part holding NULL is equal to nothing, a NULL end is open, an empty range overlaps
///         nothing, and only the rows the rule's <c>Where(…)</c> (and the soft delete) keep count. The key compares under
///         its column's collation, the case-insensitive one through <c>UseRaskSqlite</c>, as SQL Server's does.
///     </para>
///     <para>
///         Written at the END of every migration that touches the table, and always dropped before they are created: the
///         provider rebuilds a table to alter it, which takes its triggers with it.
///     </para>
/// </remarks>
internal static class NoOverlapDdl
{
    /// <summary>What the trigger's <c>RAISE</c> says before the rule's name — and what its refusal is recognised by.</summary>
    internal const string Marker = "rask no overlap: ";

    /// <summary>The commands re-establishing every rule on the tables this migration touches, or none.</summary>
    public static IReadOnlyList<MigrationCommand> Build(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model,
        MigrationsSqlGeneratorDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(dependencies);

        if (model is null)
        {
            return [];
        }

        // A table this migration drops must not have its triggers recreated; one it never touched already has them.
        var dropped = operations.OfType<DropTableOperation>().Select(static o => o.Name).ToHashSet(StringComparer.Ordinal);
        var touched = operations.Select(TableOf).OfType<string>().ToHashSet(StringComparer.Ordinal);

        var builder = new MigrationCommandListBuilder(dependencies);
        var emitted = DropStale(builder, operations);

        foreach (var entityType in model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (table is null || dropped.Contains(table) || !touched.Contains(table))
            {
                continue;
            }

            foreach (var rule in NoOverlapRule.Of(entityType))
            {
                Emit(builder, entityType, table, rule);
                emitted = true;
            }
        }

        return emitted ? builder.GetCommandList() : [];
    }

    // The triggers and indexes of a rule that changed or went away — reported on the table (RaskSqliteAnnotationProvider),
    // so either is an AlterTableOperation — and of a table renamed, which SQLite carries across ALTER TABLE … RENAME.
    private static bool DropStale(MigrationCommandListBuilder builder, IReadOnlyList<MigrationOperation> operations)
    {
        var emitted = false;
        foreach (var operation in operations)
        {
            var was = operation switch
            {
                AlterTableOperation alter when !string.Equals(
                    alter.OldTable[NoOverlapRule.Annotation] as string, alter[NoOverlapRule.Annotation] as string, StringComparison.Ordinal)
                    => alter.OldTable[NoOverlapRule.Annotation] as string,
                RenameTableOperation rename => rename[NoOverlapRule.Annotation] as string,
                _ => null,
            };

            foreach (var rule in was is null ? [] : NoOverlapRule.Parse(was))
            {
                Drop(builder, rule);
                emitted = true;
            }
        }

        return emitted;
    }

    private static void Drop(MigrationCommandListBuilder builder, NoOverlapRule rule)
    {
        Append(builder, $"DROP TRIGGER IF EXISTS {Quote(rule.Name + "_Insert")};");
        Append(builder, $"DROP TRIGGER IF EXISTS {Quote(rule.Name + "_Update")};");
        Append(builder, $"DROP INDEX IF EXISTS {Quote("ix_" + rule.Name[3..])};");
    }

    private static void Emit(MigrationCommandListBuilder builder, IEntityType entityType, string table, NoOverlapRule rule)
    {
        var key = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"'{entityType.DisplayName()}' declares NoOverlap but has no primary key, so a row cannot be told apart from itself.");
        var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());
        var primary = key.Properties.Select(p => p.GetColumnName(store) ?? p.GetColumnName()).ToArray();

        var live = rule.Rows.Length > 0 ? $" AND {rule.Rows}" : "";
        var same = string.Concat(rule.Keys.Select(static k => $" AND x.{Quote(k)} = n.{Quote(k)}"));
        var self = string.Join(" AND ", primary.Select(static k => $"x.{Quote(k)} = n.{Quote(k)}"));
        var row = string.Join(" AND ", primary.Select(static k => $"{Quote(k)} = NEW.{Quote(k)}"));
        var (from, to) = (Quote(rule.From), Quote(rule.To));
        var message = $"'{(Marker + rule.Name).Replace("'", "''", StringComparison.Ordinal)}'";

        var body =
            $"  SELECT RAISE(ABORT, {message}) WHERE EXISTS (\n" +
            $"    SELECT 1\n" +
            $"    FROM (SELECT * FROM {Quote(table)} WHERE {row}{live}) AS n\n" +
            $"    JOIN (SELECT * FROM {Quote(table)} WHERE 1 = 1{live}) AS x ON NOT ({self}){same}\n" +
            $"    WHERE (n.{to} IS NULL OR n.{from} < n.{to}) AND (x.{to} IS NULL OR x.{from} < x.{to})\n" +
            $"      AND (n.{to} IS NULL OR x.{from} < n.{to}) AND (x.{to} IS NULL OR n.{from} < x.{to}));";

        Drop(builder, rule);
        Append(builder, $"CREATE INDEX {Quote("ix_" + rule.Name[3..])} ON {Quote(table)} ({string.Join(", ", rule.Keys.Append(rule.From).Select(Quote))});");
        Append(builder, $"CREATE TRIGGER {Quote(rule.Name + "_Insert")} AFTER INSERT ON {Quote(table)}\nBEGIN\n{body}\nEND;");
        Append(builder, $"CREATE TRIGGER {Quote(rule.Name + "_Update")} AFTER UPDATE ON {Quote(table)}\nBEGIN\n{body}\nEND;");
    }

    private static void Append(MigrationCommandListBuilder builder, string sql)
    {
        builder.AppendLines(sql);
        builder.EndCommand();
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    internal static string? TableOf(MigrationOperation operation) => operation switch
    {
        CreateTableOperation create => create.Name,
        DropTableOperation drop => drop.Name,
        AlterTableOperation alter => alter.Name,
        RenameTableOperation rename => rename.NewName ?? rename.Name,
        AddColumnOperation add => add.Table,
        AlterColumnOperation alter => alter.Table,
        DropColumnOperation drop => drop.Table,
        RenameColumnOperation rename => rename.Table,
        CreateIndexOperation create => create.Table,
        DropIndexOperation drop => drop.Table,
        AddUniqueConstraintOperation add => add.Table,
        DropUniqueConstraintOperation drop => drop.Table,
        AddCheckConstraintOperation add => add.Table,
        DropCheckConstraintOperation drop => drop.Table,
        AddPrimaryKeyOperation add => add.Table,
        DropPrimaryKeyOperation drop => drop.Table,
        AddForeignKeyOperation add => add.Table,
        DropForeignKeyOperation drop => drop.Table,
        _ => null,
    };
}
