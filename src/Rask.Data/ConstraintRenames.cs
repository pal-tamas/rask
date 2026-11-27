using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     A primary key, foreign key or unique constraint a migration drops and adds again under a new name — what EF Core's
///     differ writes for a renamed constraint, as it has no operation that renames one — renamed instead.
/// </summary>
/// <remarks>
///     <para>
///         A pair is a rename when the two are the same constraint of the same entity types, over the same properties, with
///         the same delete rule: only the name, the table's name or the columns' names changed — what Rask's
///         <c>pk_</c>/<c>fk_</c>/<c>uq_</c> names do to an existing database. Anything more keeps EF Core's drop and add.
///     </para>
///     <para>
///         The rename is the database's own statement, where the add was, so after the table and its columns are renamed:
///         <c>ALTER TABLE … RENAME CONSTRAINT</c> on PostgreSQL, <c>sp_rename … 'OBJECT'</c> on SQL Server. SQLite names
///         a constraint only inside its <c>CREATE TABLE</c> and checks none by name, so there the pair is left out: the
///         table is not rebuilt for a name. A column whose only change is its default's name — SQL Server's <c>df_</c>,
///         or the name the server chose itself — is renamed there too, found by its column, and left out elsewhere.
///     </para>
/// </remarks>
internal static class ConstraintRenames
{
    /// <summary><paramref name="operations" /> with every renamed constraint renamed rather than dropped and added.</summary>
    public static List<MigrationOperation> Apply(
        List<MigrationOperation> operations,
        IRelationalModel source,
        IRelationalModel target,
        string provider,
        ISqlGenerationHelper sql)
    {
        var drops = new Dictionary<string, Queue<MigrationOperation>>(StringComparer.Ordinal);
        foreach (var drop in operations.Where(static o => o is DropPrimaryKeyOperation or DropUniqueConstraintOperation or DropForeignKeyOperation))
        {
            if (Identity(source, drop) is not { } identity)
            {
                continue;
            }

            if (!drops.TryGetValue(identity, out var same))
            {
                same = new Queue<MigrationOperation>();
                drops[identity] = same;
            }

            same.Enqueue(drop);
        }

        var renamed = new Dictionary<MigrationOperation, MigrationOperation?>();
        foreach (var add in operations.Where(static o => o is AddPrimaryKeyOperation or AddUniqueConstraintOperation or AddForeignKeyOperation))
        {
            if (Identity(target, add) is { } identity && drops.TryGetValue(identity, out var same) && same.Count > 0)
            {
                var drop = same.Dequeue();
                renamed[drop] = null;
                renamed[add] = Rename(drop, add, provider, sql);
            }
        }

        // A column default SQL Server names, only renamed: the constraint renamed in place, whatever the server named it.
        foreach (var altered in operations.OfType<AlterColumnOperation>().Where(static a => OnlyItsDefaultIsRenamed(a)))
        {
            renamed[altered] = string.Equals(provider, ProviderNames.SqlServer, StringComparison.Ordinal)
                ? RenameDefault(altered, sql)
                : null;
        }

        if (renamed.Count == 0)
        {
            return operations;
        }

        var result = new List<MigrationOperation>(operations.Count);
        foreach (var operation in operations)
        {
            if (!renamed.TryGetValue(operation, out var replacement))
            {
                result.Add(operation);
            }
            else if (replacement is not null)
            {
                result.Add(replacement);
            }
        }

        return result;
    }

    // What the operation drops in `source` or adds in `target`, by the model it maps: the kind, the entity types, the
    // properties, the delete rule. Null for an operation that is neither, or for a constraint the model cannot tell.
    private static string? Identity(IRelationalModel model, MigrationOperation operation) => operation switch
    {
        DropPrimaryKeyOperation drop => Key(model.FindTable(drop.Table, drop.Schema), drop.Name, primary: true),
        AddPrimaryKeyOperation add => Key(model.FindTable(add.Table, add.Schema), add.Name, primary: true),
        DropUniqueConstraintOperation drop => Key(model.FindTable(drop.Table, drop.Schema), drop.Name, primary: false),
        AddUniqueConstraintOperation add => Key(model.FindTable(add.Table, add.Schema), add.Name, primary: false),
        DropForeignKeyOperation drop => ForeignKey(model, drop.Table, drop.Schema, drop.Name),
        AddForeignKeyOperation add => ForeignKey(model, add.Table, add.Schema, add.Name),
        _ => null,
    };

    private static string? Key(ITable? table, string name, bool primary)
    {
        if (table is null)
        {
            return null;
        }

        IUniqueConstraint? key = primary
            ? table.PrimaryKey
            : table.UniqueConstraints.FirstOrDefault(u => string.Equals(u.Name, name, StringComparison.Ordinal));
        if (key is null || !string.Equals(key.Name, name, StringComparison.Ordinal))
        {
            return null;
        }

        return string.Join('|', primary ? "pk" : "uq", Entities(table), Properties(key.Columns));
    }

    private static string? ForeignKey(IRelationalModel model, string tableName, string? schema, string name)
    {
        if (model.FindTable(tableName, schema) is not { } table)
        {
            return null;
        }

        if (table.ForeignKeyConstraints.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.Ordinal)) is { } key)
        {
            return string.Join(
                '|', "fk", Entities(table), Properties(key.Columns), Entities(key.PrincipalTable), Properties(key.PrincipalColumns),
                key.OnDeleteAction.ToString());
        }

        // A tenant reference is the schema's, not the model's: its table's TenantId, to the tenant's table.
        return TenantReference.In(model).TryGetValue((tableName, schema), out var declared) &&
               string.Equals(declared.Reference.Name, name, StringComparison.Ordinal) &&
               table.FindColumn(declared.Column) is { } column &&
               model.FindTable(declared.Reference.PrincipalTable, declared.Reference.PrincipalSchema) is { } principal
            ? string.Join('|', "tenant", Entities(table), Properties([column]), Entities(principal))
            : null;
    }

    private static string Entities(ITableBase table) =>
        string.Join(',', table.EntityTypeMappings.Select(static m => m.TypeBase.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    private static string Properties(IEnumerable<IColumnBase> columns) =>
        string.Join(',', columns.Select(static c => c.PropertyMappings.Select(static m => m.Property.Name).FirstOrDefault() ?? c.Name));

    private static SqlOperation? Rename(MigrationOperation drop, MigrationOperation add, string provider, ISqlGenerationHelper sql)
    {
        var (table, schema, from, to) = Names(drop, add);
        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return null;
        }

        return provider switch
        {
            ProviderNames.Postgres => new SqlOperation
            {
                Sql = $"ALTER TABLE {sql.DelimitIdentifier(table, schema)} RENAME CONSTRAINT {sql.DelimitIdentifier(from)} TO {sql.DelimitIdentifier(to)};",
            },
            ProviderNames.SqlServer => new SqlOperation
            {
                Sql = $"EXEC sp_rename N'{Escaped(schema is null ? sql.DelimitIdentifier(from) : sql.DelimitIdentifier(from, schema))}', N'{Escaped(to)}', N'OBJECT';",
            },

            // SQLite reads no constraint by name: the old one stays in the CREATE TABLE until the table is next rebuilt.
            _ => null,
        };
    }

    private static (string Table, string? Schema, string From, string To) Names(MigrationOperation drop, MigrationOperation add) =>
        (drop, add) switch
        {
            (DropPrimaryKeyOperation d, AddPrimaryKeyOperation a) => (a.Table, a.Schema, d.Name, a.Name),
            (DropUniqueConstraintOperation d, AddUniqueConstraintOperation a) => (a.Table, a.Schema, d.Name, a.Name),
            (DropForeignKeyOperation d, AddForeignKeyOperation a) => (a.Table, a.Schema, d.Name, a.Name),
            _ => throw new InvalidOperationException($"'{drop.GetType().Name}' and '{add.GetType().Name}' are not one constraint."),
        };

    private static bool OnlyItsDefaultIsRenamed(AlterColumnOperation altered)
    {
        var old = altered.OldColumn;
        return old.ClrType == altered.ClrType &&
               string.Equals(old.ColumnType, altered.ColumnType, StringComparison.Ordinal) &&
               old.IsNullable == altered.IsNullable &&
               Equals(old.DefaultValue, altered.DefaultValue) &&
               string.Equals(old.DefaultValueSql, altered.DefaultValueSql, StringComparison.Ordinal) &&
               string.Equals(old.ComputedColumnSql, altered.ComputedColumnSql, StringComparison.Ordinal) &&
               old.IsStored == altered.IsStored &&
               string.Equals(old.Collation, altered.Collation, StringComparison.Ordinal) &&
               string.Equals(old.Comment, altered.Comment, StringComparison.Ordinal) &&
               old.MaxLength == altered.MaxLength && old.IsUnicode == altered.IsUnicode &&
               old.IsFixedLength == altered.IsFixedLength && old.IsRowVersion == altered.IsRowVersion &&
               old.Precision == altered.Precision && old.Scale == altered.Scale &&
               SameAnnotationsBesideTheDefaultsName(old, altered) &&
               !Equals(old[RelationalAnnotationNames.DefaultConstraintName], altered[RelationalAnnotationNames.DefaultConstraintName]) &&
               altered[RelationalAnnotationNames.DefaultConstraintName] is string;
    }

    private static bool SameAnnotationsBesideTheDefaultsName(ColumnOperation old, ColumnOperation altered)
    {
        static Dictionary<string, object?> Of(ColumnOperation column) =>
            column.GetAnnotations().Where(static a => !string.Equals(a.Name, RelationalAnnotationNames.DefaultConstraintName, StringComparison.Ordinal))
                .ToDictionary(static a => a.Name, static a => a.Value, StringComparer.Ordinal);

        var before = Of(old);
        var after = Of(altered);
        return before.Count == after.Count && before.All(a => after.TryGetValue(a.Key, out var value) && Equals(a.Value, value));
    }

    // The server may have named the default itself (DF__fuelings__flagg__3B75D760), so it is found by its column.
    private static SqlOperation RenameDefault(AlterColumnOperation altered, ISqlGenerationHelper sql)
    {
        var table = sql.DelimitIdentifier(altered.Table, altered.Schema);
        var to = (string)altered[RelationalAnnotationNames.DefaultConstraintName]!;
        return new SqlOperation
        {
            Sql = $"""
                DECLARE @default sysname = (SELECT QUOTENAME(SCHEMA_NAME(d.schema_id)) + N'.' + QUOTENAME(d.name)
                    FROM sys.default_constraints d
                    JOIN sys.columns c ON c.object_id = d.parent_object_id AND c.column_id = d.parent_column_id
                    WHERE d.parent_object_id = OBJECT_ID(N'{Escaped(table)}') AND c.name = N'{Escaped(altered.Name)}');
                IF @default IS NOT NULL EXEC sp_rename @default, N'{Escaped(to)}', N'OBJECT';
                """,
        };
    }

    private static string Escaped(string text) => text.Replace("'", "''", StringComparison.Ordinal);
}
