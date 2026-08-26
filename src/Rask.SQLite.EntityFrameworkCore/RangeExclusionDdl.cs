using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
/// Builds the SQLite DDL enforcing each <see cref="RangeExclusionSpec"/> a model declares: an index plus a
/// matching pair of <c>BEFORE INSERT</c> / <c>BEFORE UPDATE</c> triggers that <c>RAISE(ABORT)</c>.
/// </summary>
/// <remarks>
/// <para>
/// SQLite has no exclusion constraint, and a unique index cannot express overlap — it only stops identical
/// rows. Triggers are the only mechanism that makes the rule a real constraint rather than an application
/// convention.
/// </para>
/// <para>
/// This lives apart from any one generator because a context can need more than one thing from its migration
/// SQL — strict tables and range exclusion, say — and C# has no multiple inheritance, so each generator
/// composes this instead of subclassing its way to both.
/// </para>
/// </remarks>
internal static class RangeExclusionDdl
{
    /// <summary>
    /// Returns the commands re-establishing every declared rule on the tables this migration touches, or an
    /// empty list when the model declares none.
    /// </summary>
    /// <param name="operations">The migration's operations.</param>
    /// <param name="model">The target model, or <see langword="null"/> when the migration has none.</param>
    /// <param name="dependencies">The generator's dependencies, used to build the commands.</param>
    /// <returns>The commands to append after the migration's own SQL.</returns>
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

        // A table this migration drops must not have its triggers recreated; one it never touched already
        // has them from an earlier migration.
        var dropped = operations.OfType<DropTableOperation>()
            .Select(operation => operation.Name)
            .ToHashSet(StringComparer.Ordinal);

        var touched = operations.Select(TableOf)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal)!;

        var builder = new MigrationCommandListBuilder(dependencies);
        var emitted = false;

        // SQLite carries a trigger across ALTER TABLE ... RENAME, so the pre-rename pair would survive under
        // its old name and double up with the pair emitted below.
        foreach (var rename in operations.OfType<RenameTableOperation>())
        {
            Append(builder, $"DROP TRIGGER IF EXISTS {Quote($"TR_{rename.Name}_NoOverlap_Insert")};");
            Append(builder, $"DROP TRIGGER IF EXISTS {Quote($"TR_{rename.Name}_NoOverlap_Update")};");
            emitted = true;
        }

        foreach (var entityType in model.GetEntityTypes())
        {
            if (!RangeExclusionSpec.TryParse(entityType.FindAnnotation(RangeExclusionSpec.AnnotationName)?.Value, out var spec))
            {
                continue;
            }

            var table = entityType.GetTableName();
            if (table is null || dropped.Contains(table) || !touched.Contains(table))
            {
                continue;
            }

            Emit(builder, entityType, table, spec);
            emitted = true;
        }

        return emitted ? builder.GetCommandList() : [];
    }

    private static void Emit(
        MigrationCommandListBuilder builder,
        IEntityType entityType,
        string table,
        RangeExclusionSpec spec)
    {
        var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());

        var lo = Column(entityType, spec.Lo, store);
        var hi = Column(entityType, spec.Hi, store);
        var partition = spec.PartitionBy.Select(property => Column(entityType, property, store)).ToArray();

        var key = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"'{entityType.DisplayName()}' declares a non-overlapping range but has no primary key, so a " +
                "row cannot be told apart from itself when the rule is checked.");

        var keyColumns = key.Properties.Select(property => Column(entityType, property.Name, store)).ToArray();

        // Only a soft-deletable entity has the column; the builder already refuses the flag otherwise.
        var deletedAt = spec.IgnoreSoftDeleted
            ? Column(entityType, nameof(ISoftDeletable.DeletedAt), store)
            : null;

        var indexName = $"IX_{table}_Range";
        var insertTrigger = $"TR_{table}_NoOverlap_Insert";
        var updateTrigger = $"TR_{table}_NoOverlap_Update";

        var indexColumns = string.Join(", ", partition.Append(lo).Append(hi).Select(Quote));
        var watchedColumns = string.Join(
            ", ",
            partition.Append(lo).Append(hi).Concat(deletedAt is null ? [] : new[] { deletedAt }).Select(Quote));

        // `IS NOT` rather than `<>` so a NULL partition value still compares as equal.
        var scope = string.Concat(partition.Select(column => $"x.{Quote(column)} IS NEW.{Quote(column)} AND "));

        // The row must not be compared against itself: BEFORE INSERT also fires for the insert half of an
        // upsert or INSERT OR REPLACE, where the conflicting row IS the row being written.
        var notSelf = string.Join(
            " OR ",
            keyColumns.Select(column => $"x.{Quote(column)} IS NOT NEW.{Quote(column)}"));

        var live = deletedAt is null
            ? string.Empty
            : $" AND x.{Quote(deletedAt)} IS NULL AND NEW.{Quote(deletedAt)} IS NULL";

        var body =
            $"  SELECT RAISE(ABORT, {Literal($"{table}: range overlaps an existing row")})\n" +
            $"  WHERE NEW.{Quote(lo)} IS NOT NULL AND NEW.{Quote(hi)} IS NOT NULL AND EXISTS (\n" +
            $"    SELECT 1 FROM {Quote(table)} x\n" +
            $"     WHERE {scope}({notSelf})\n" +
            $"       AND x.{Quote(lo)} < NEW.{Quote(hi)} AND NEW.{Quote(lo)} < x.{Quote(hi)}{live});";

        Append(builder, $"DROP TRIGGER IF EXISTS {Quote(insertTrigger)};");
        Append(builder, $"DROP TRIGGER IF EXISTS {Quote(updateTrigger)};");
        Append(builder, $"CREATE INDEX IF NOT EXISTS {Quote(indexName)} ON {Quote(table)} ({indexColumns});");
        Append(builder, $"CREATE TRIGGER {Quote(insertTrigger)} BEFORE INSERT ON {Quote(table)}\nBEGIN\n{body}\nEND;");
        Append(builder, $"CREATE TRIGGER {Quote(updateTrigger)} BEFORE UPDATE OF {watchedColumns} ON {Quote(table)}\nBEGIN\n{body}\nEND;");
    }

    private static void Append(MigrationCommandListBuilder builder, string sql)
    {
        builder.AppendLines(sql);
        builder.EndCommand();
    }

    private static string Column(IEntityType entityType, string property, StoreObjectIdentifier store)
    {
        var mapped = entityType.FindProperty(property)
            ?? throw new InvalidOperationException(
                $"'{entityType.DisplayName()}' declares a non-overlapping range over '{property}', which is not " +
                "a mapped property. Check the name, and that the property is not ignored.");

        return mapped.GetColumnName(store)
            ?? throw new InvalidOperationException(
                $"'{entityType.DisplayName()}.{property}' is not mapped to a column of '{store.Name}', so a " +
                "non-overlapping range rule cannot be enforced over it.");
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string Literal(string value)
        => string.Create(CultureInfo.InvariantCulture, $"'{value.Replace("'", "''", StringComparison.Ordinal)}'");

    private static string? TableOf(MigrationOperation operation) => operation switch
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
