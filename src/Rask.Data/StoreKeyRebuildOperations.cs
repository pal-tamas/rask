using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     The rebuild of a table whose key a migration turns from a store-numbered integer into a client-made Guid
///     (<see cref="StoreKeyRebuild" />), in place of every operation EF Core's differ wrote for that table.
/// </summary>
/// <remarks>
///     <para>
///         No provider can change an identity column into a Guid in place — SQL Server refuses to alter an identity,
///         PostgreSQL has no cast from <c>bigint</c> to <c>uuid</c>, and SQLite's table rebuild would copy <c>1</c>, <c>2</c>,
///         <c>3</c> in as ids — so the table is dropped and created again from the target model, its indexes with it:
///     </para>
///     <list type="number">
///         <item>the guard, which fails the migration while a row is still pending — opened by the exclusive marker, so
///             <c>rask deploy</c> stops the old version first and nothing is enqueued under the old key meanwhile;</item>
///         <item>the kept rows set aside under their new ids, when the table keeps any;</item>
///         <item>the table dropped and created again, with its indexes;</item>
///         <item>the kept rows written back.</item>
///     </list>
/// </remarks>
internal static class StoreKeyRebuildOperations
{
    private const string UniqueIdentifier = "uniqueidentifier";

    /// <summary>
    ///     <paramref name="operations" />, with every table <see cref="StoreKeyRebuild" /> marks whose key turns from an
    ///     integer into a Guid rebuilt instead of altered.
    /// </summary>
    /// <param name="operations">What EF Core's differ wrote.</param>
    /// <param name="source">The model the database has.</param>
    /// <param name="target">The model it moves to.</param>
    /// <param name="created">Every operation that creates the target model from nothing, asked for only when a table is rebuilt.</param>
    /// <param name="provider">The provider's name.</param>
    /// <param name="sql">The provider's SQL helper.</param>
    /// <param name="guidOrderOn">Whether SQL Server stores the target's Guids by time; and the source's.</param>
    internal static IReadOnlyList<MigrationOperation> Apply(
        IReadOnlyList<MigrationOperation> operations,
        IRelationalModel? source,
        IRelationalModel target,
        Func<IReadOnlyList<MigrationOperation>> created,
        string provider,
        ISqlGenerationHelper sql,
        (bool Source, bool Target) guidOrderOn)
    {
        if (source is null)
        {
            return operations;
        }

        var result = operations;
        IReadOnlyList<MigrationOperation>? creation = null;
        foreach (var table in target.Tables)
        {
            if (StoreKeyRebuild.Of(table) is not { } rebuild ||
                source.FindTable(table.Name, table.Schema) is not { } before ||
                KeyOf(table) is not { } key || KeyOf(before) is not { } was ||
                !string.Equals(key.Name, was.Name, StringComparison.Ordinal) ||
                !IsInteger(was.ProviderClrType) || !IsGuid(key) ||
                Recreates(result, table))
            {
                continue;
            }

            creation ??= created();
            result = Rebuilt(result, table, before, key.Name, rebuild, creation, new StoreKeyRebuildSql(provider, sql, table.Name, table.Schema, IsText(key)), guidOrderOn);
        }

        return result;
    }

    private static List<MigrationOperation> Rebuilt(
        IReadOnlyList<MigrationOperation> operations,
        ITable table,
        ITable before,
        string key,
        StoreKeyRebuild rebuild,
        IReadOnlyList<MigrationOperation> creation,
        StoreKeyRebuildSql sql,
        (bool Source, bool Target) guidOrderOn)
    {
        var kept = table.Columns
            .Where(c => c.ComputedColumnSql is null && !string.Equals(c.Name, key, StringComparison.Ordinal) &&
                        before.FindColumn(c.Name) is not null)
            .ToList();
        var columns = kept.Select(static c => c.Name).ToList();
        var guids = kept.Where(static c => string.Equals(c.StoreType, UniqueIdentifier, StringComparison.OrdinalIgnoreCase))
            .Select(static c => c.Name)
            .ToHashSet(StringComparer.Ordinal);

        // A table created by the migration is no table the Guid-order rewrite reads, so on SQL Server the rows come back
        // in the form the target stores: rewritten here when the same migration starts storing by time.
        var rewrite = guidOrderOn.Target && !guidOrderOn.Source;
        string Read(string column)
        {
            var name = "rask_rows." + sql.Delimit(column);
            return rewrite && guids.Contains(column) ? SqlServerGuidOrder.StoredFromNative(name) : name;
        }

        var first = new List<string> { sql.Marker };
        if (rebuild.Pending is { } pending)
        {
            first.Add(sql.Guard(ColumnOf(before, pending)));
        }

        if (rebuild.KeepBy is { } keepBy)
        {
            first.Add(sql.SetAside(key, ColumnOf(before, keepBy), guidOrderOn.Target, columns, Read));
        }

        var rebuilt = new List<MigrationOperation>
        {
            new SqlOperation { Sql = string.Join("\n", first) },
            new DropTableOperation { Name = table.Name, Schema = table.Schema },
        };
        rebuilt.AddRange(creation.Where(o => Touches(o, table) && o is not InsertDataOperation));
        if (rebuild.KeepBy is not null)
        {
            rebuilt.Add(new SqlOperation { Sql = sql.Restore(key, columns) });
        }

        rebuilt.AddRange(creation.Where(o => o is InsertDataOperation && Touches(o, table)));

        var at = operations.ToList().FindIndex(o => Touches(o, table));
        var rest = operations.Where(o => !Touches(o, table)).ToList();
        rest.InsertRange(at < 0 ? rest.Count : at, rebuilt);
        return rest;
    }

    // The single key column of a table, or null for a composite or keyless one.
    private static IColumn? KeyOf(ITable table) =>
        table.PrimaryKey is { Columns.Count: 1 } primary ? primary.Columns[0] : null;

    // The column a property of the table's entity is stored in, read from the model the database has.
    private static string ColumnOf(ITable table, string property) =>
        table.EntityTypeMappings
            .Select(m => m.TypeBase.FindProperty(property)?.GetColumnName(StoreObjectIdentifier.Table(table.Name, table.Schema)))
            .FirstOrDefault(static c => c is not null)
        ?? property;

    // A Guid, or a typed id over one, which Rask writes as lower-case text where the column is text (GuidIdTextConvention).
    private static bool IsGuid(IColumn key) =>
        key.ProviderClrType == typeof(Guid) || (IsText(key) && key.PropertyMappings.All(static m => m.Property.ClrType != typeof(string)));

    private static bool IsText(IColumn key) => key.ProviderClrType == typeof(string);

    private static bool IsInteger(Type type) => type == typeof(long) || type == typeof(int) || type == typeof(short);

    // Created or dropped by the migration anyway: nothing of its rows to keep.
    private static bool Recreates(IReadOnlyList<MigrationOperation> operations, ITable table) =>
        operations.Any(o => o is CreateTableOperation or DropTableOperation && Touches(o, table));

    private static bool Touches(MigrationOperation operation, ITable table) => operation switch
    {
        TableOperation t => Same(t.Name, t.Schema, table),
        DropTableOperation d => Same(d.Name, d.Schema, table),
        RenameTableOperation r => Same(r.Name, r.Schema, table) || Same(r.NewName ?? r.Name, r.NewSchema, table),
        ITableMigrationOperation t => Same(t.Table, t.Schema, table),
        _ => false,
    };

    private static bool Same(string name, string? schema, ITable table) =>
        string.Equals(name, table.Name, StringComparison.Ordinal) && string.Equals(schema, table.Schema, StringComparison.Ordinal);
}
