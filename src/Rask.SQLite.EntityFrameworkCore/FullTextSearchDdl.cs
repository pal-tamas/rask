using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
/// Builds the SQLite DDL behind each <see cref="FullTextSearchSpec"/> a model declares: an FTS5 virtual table, the
/// triggers that keep it in step with its table, and the statement that fills it from the rows already there.
/// </summary>
/// <remarks>
/// <para>
/// <b>The index keeps its own copy of the text.</b> An <em>external-content</em> FTS5 table would store nothing twice,
/// but it can only forget a row when told that row's old values — and SQLite does not fire <c>AFTER DELETE</c> for
/// the row an <c>INSERT OR REPLACE</c> replaces (not without <c>recursive_triggers</c>), so one raw <c>REPLACE</c>
/// would leave the index permanently wrong. A regular FTS5 table forgets by rowid alone, so every trigger clears
/// whatever the index holds for a row before writing it, and REPLACE, upserts and key changes all stay correct. The
/// cost is disk roughly the size of the indexed columns.
/// </para>
/// <para>
/// <b>Two ways to find a row's index entry, chosen by the primary key.</b> A single <c>INTEGER</c> key IS the index
/// rowid. Any other key — a <see cref="Guid"/>, a string, a composite — goes through a small key map
/// (<c>{Table}_fts_keys</c>) whose unique index resolves it in one seek: SQLite's implicit rowid is not stable across
/// <c>VACUUM</c>, so the index cannot point at it. The choice is recorded on the entity type by
/// <see cref="FullTextSearchEntityConvention"/>, so a migration — which runs against the model it was generated
/// from, not the live one — builds exactly the layout the queries join to.
/// </para>
/// <para>
/// <b>Triggers, not SaveChanges.</b> The index is kept current by <c>AFTER INSERT/UPDATE/DELETE</c> triggers, so a
/// row written by raw SQL, <c>ExecuteUpdate</c>, a bulk insert or another process is searchable the moment it
/// commits. The triggers call no function, so they run under Rask's default <c>trusted_schema=OFF</c>.
/// </para>
/// <para>
/// <b>Recreated whenever its table is touched.</b> SQLite rebuilds a table for most <c>ALTER</c>s, which drops its
/// triggers, and a renamed column would leave the triggers writing a column that no longer exists.
/// Rather than tell those cases apart, any migration that touches a searchable table drops the index and builds it
/// again from the table — correct whichever path the provider took, at the cost of re-reading the table.
/// </para>
/// </remarks>
internal static class FullTextSearchDdl
{
    /// <summary>The FTS5 virtual table behind <paramref name="table"/>.</summary>
    public static string IndexTable(string table) => $"{table}_fts";

    /// <summary>The key map behind a table whose key is not a single <c>INTEGER</c> column.</summary>
    public static string KeyTable(string table) => $"{table}_fts_keys";

    /// <summary>The annotation recording which layout an entity's index uses: <see cref="RowidLayout"/> or <see cref="KeyMapLayout"/>.</summary>
    public const string LayoutAnnotation = "Rask:FullTextSearch:Layout";

    public const string RowidLayout = "rowid";

    public const string KeyMapLayout = "keys";

    /// <summary>
    /// Whether <paramref name="entityType"/>'s index points straight at its rowid (a single <c>INTEGER</c> key),
    /// rather than through a key map.
    /// </summary>
    /// <remarks>
    /// The recorded <see cref="LayoutAnnotation"/> wins. It has to: a migration runs against the model saved with it,
    /// where a converted key (a strongly-typed id, an enum) is already its provider type, while the queries run
    /// against the live model — deciding afresh on each would let them disagree, and a search would join to a key map
    /// the migration never created. The rule itself reads the configured column type, or else the provider type after
    /// any value converter, because the convention asks before type mappings exist.
    /// </remarks>
    public static bool UsesRowid(IReadOnlyEntityType entityType)
    {
        if (entityType.FindAnnotation(LayoutAnnotation)?.Value is string recorded)
        {
            return recorded == RowidLayout;
        }

        if (entityType.FindPrimaryKey() is not { Properties: [var key] })
        {
            return false;
        }

        if (key.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value is string columnType)
        {
            return string.Equals(columnType, "INTEGER", StringComparison.OrdinalIgnoreCase);
        }

        var converter = key.GetValueConverter()
            ?? (ValueConverterTypeOf(key) is { } converterType
                ? (Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter?)Activator.CreateInstance(converterType)
                : null);

        var type = converter?.ProviderClrType ?? key.ClrType;
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
        {
            type = Enum.GetUnderlyingType(type);
        }

        return type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
            || type == typeof(uint) || type == typeof(ushort) || type == typeof(sbyte) || type == typeof(ulong);
    }

    /// <summary>
    /// The converter TYPE a property was configured with (<c>HaveConversion&lt;TConverter&gt;()</c>, which is how Rask.Data
    /// maps strongly-typed ids), or <see langword="null"/>. EF Core stores it under this core annotation and exposes no
    /// public accessor for it on the read-only metadata.
    /// </summary>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    [UnconditionalSuppressMessage("Trimming", "IL2073:UnrecognizedReflectionPattern",
        Justification = "The annotation only ever holds a type EF Core stored from HasConversion<TConverter>() or "
                        + "HasConversion(Type), both of which keep the converter's public parameterless constructor.")]
    public static Type? ValueConverterTypeOf(IReadOnlyAnnotatable property) =>
        property.FindAnnotation("ValueConverterType")?.Value as Type;

    /// <summary>The first entity type mapped to <paramref name="table"/> that declares an index, with its spec.</summary>
    public static (IEntityType EntityType, FullTextSearchSpec Spec)? Find(IModel model, string table, string? schema)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.GetTableName() == table
                && entityType.GetSchema() == schema
                && FullTextSearchSpec.TryParse(entityType.FindAnnotation(FullTextSearchSpec.AnnotationName)?.Value, out var spec))
            {
                return (entityType, spec);
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the commands creating, recreating or removing the index of every searchable table this migration
    /// touches, or an empty list when it touches none.
    /// </summary>
    /// <param name="operations">The migration's operations.</param>
    /// <param name="model">The target model, or <see langword="null"/> when the migration has none.</param>
    /// <param name="dependencies">The generator's dependencies, used to build the commands.</param>
    public static IReadOnlyList<MigrationCommand> Build(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model,
        MigrationsSqlGeneratorDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(dependencies);

        var builder = new MigrationCommandListBuilder(dependencies);
        var emitted = false;

        var dropped = new HashSet<string>(StringComparer.Ordinal);
        var rebuild = new List<(string Table, string? Schema)>();

        foreach (var operation in operations)
        {
            switch (operation)
            {
                case DropTableOperation drop:
                    // The virtual table outlives the table it indexes (the triggers go with the table), and the
                    // target model no longer says whether there was one — so any dropped table clears its own.
                    dropped.Add(drop.Name);
                    Drop(builder, drop.Name);
                    emitted = true;
                    break;

                case RenameTableOperation rename:
                    // Triggers travel with ALTER TABLE ... RENAME under their old names, and the index keeps the old
                    // table's name, so the old set is cleared before the new one is built.
                    Drop(builder, rename.Name);
                    emitted = true;
                    Touch(rebuild, rename.NewName ?? rename.Name, rename.NewSchema ?? rename.Schema);
                    break;

                case AlterTableOperation alter
                    when alter.OldTable[FullTextSearchSpec.AnnotationName] is not null
                         && alter[FullTextSearchSpec.AnnotationName] is null:
                    // The declaration was removed.
                    Drop(builder, alter.Name);
                    emitted = true;
                    break;

                // Adding or dropping a plain index never rebuilds the table, so its triggers and index survive.
                case CreateIndexOperation or DropIndexOperation:
                    break;

                default:
                    if (TableOf(operation) is { } touched)
                    {
                        Touch(rebuild, touched.Table, touched.Schema);
                    }

                    break;
            }
        }

        if (model is not null)
        {
            foreach (var (table, schema) in rebuild)
            {
                if (dropped.Contains(table) || Find(model, table, schema) is not { } found)
                {
                    continue;
                }

                Create(builder, found.EntityType, table, schema, found.Spec);
                emitted = true;
            }
        }

        return emitted ? builder.GetCommandList() : [];
    }

    private static void Touch(List<(string Table, string? Schema)> tables, string table, string? schema)
    {
        if (!tables.Contains((table, schema)))
        {
            tables.Add((table, schema));
        }
    }

    private static void Drop(MigrationCommandListBuilder builder, string table)
    {
        foreach (var trigger in Triggers(table))
        {
            Append(builder, $"DROP TRIGGER IF EXISTS {Quote(trigger)};");
        }

        Append(builder, $"DROP TABLE IF EXISTS {Quote(IndexTable(table))};");
        Append(builder, $"DROP TABLE IF EXISTS {Quote(KeyTable(table))};");
    }

    private static void Create(
        MigrationCommandListBuilder builder,
        IEntityType entityType,
        string table,
        string? schema,
        FullTextSearchSpec spec)
    {
        var store = StoreObjectIdentifier.Table(table, schema);

        var columns = spec.Properties.Select(property => Column(entityType, property, store)).ToArray();
        var key = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"'{entityType.DisplayName()}' declares HasFullTextSearch but has no primary key, so a search match " +
                "cannot be joined back to its row.");
        var keyColumns = key.Properties.Select(property => Column(entityType, property.Name, store)).ToArray();

        var index = Quote(IndexTable(table));
        var source = Quote(table);
        var tokenize = Literal(Tokenizer(spec.Tokenizer));
        var columnList = string.Join(", ", columns.Select(Quote));
        var watched = string.Join(", ", columns.Concat(keyColumns).Distinct(StringComparer.Ordinal).Select(Quote));
        var (insert, delete, update) = (Triggers(table)[0], Triggers(table)[1], Triggers(table)[2]);

        Drop(builder, table);

        // Every write first clears whatever the index holds for the row, by rowid. That is what keeps a REPLACE (whose
        // replaced row fires no AFTER DELETE), an upsert and a key change from leaving a stale or duplicate entry.
        Append(builder, $"CREATE VIRTUAL TABLE {index} USING fts5({columnList}, tokenize={tokenize});");

        if (UsesRowid(entityType))
        {
            var rowid = Quote(keyColumns[0]);
            string Write(string row) =>
                $"  DELETE FROM {index} WHERE rowid = {row}.{rowid};\n" +
                $"  INSERT INTO {index}(rowid, {columnList}) VALUES ({row}.{rowid}, {Values(row, columns)});\n";

            Append(builder, $"CREATE TRIGGER {Quote(insert)} AFTER INSERT ON {source}\nBEGIN\n{Write("NEW")}END;");
            Append(builder, $"CREATE TRIGGER {Quote(delete)} AFTER DELETE ON {source}\nBEGIN\n  DELETE FROM {index} WHERE rowid = OLD.{rowid};\nEND;");
            Append(
                builder,
                $"CREATE TRIGGER {Quote(update)} AFTER UPDATE OF {watched} ON {source}\nBEGIN\n" +
                $"  DELETE FROM {index} WHERE rowid = OLD.{rowid};\n{Write("NEW")}END;");
            Append(builder, $"INSERT INTO {index}(rowid, {columnList}) SELECT {rowid}, {columnList} FROM {source};");
            return;
        }

        var keys = Quote(KeyTable(table));
        var keyDefinitions = string.Join(", ", key.Properties.Select((property, i) =>
            $"{Quote(keyColumns[i])} {property.GetColumnType(store)} NOT NULL"));
        var keyList = string.Join(", ", keyColumns.Select(Quote));
        string Match(string row) => string.Join(" AND ", keyColumns.Select(c => $"{Quote(c)} = {row}.{Quote(c)}"));
        string Lookup(string row) => $"(SELECT rowid FROM {keys} WHERE {Match(row)})";
        // Clear through the EXISTING mapping before touching the map: inside an INSERT OR REPLACE, SQLite applies the
        // outer statement's conflict resolution to the trigger's own statements, so the OR IGNORE below acts as a
        // REPLACE and gives the key a new rowid — which would orphan the entry filed under the old one.
        string WriteKeyed(string row) =>
            $"  DELETE FROM {index} WHERE rowid = {Lookup(row)};\n" +
            $"  INSERT OR IGNORE INTO {keys}({keyList}) VALUES ({Values(row, keyColumns)});\n" +
            $"  INSERT INTO {index}(rowid, {columnList}) VALUES ({Lookup(row)}, {Values(row, columns)});\n";
        string Forget(string row) =>
            $"  DELETE FROM {index} WHERE rowid = {Lookup(row)};\n" +
            $"  DELETE FROM {keys} WHERE {Match(row)};\n";

        Append(builder, $"CREATE TABLE {keys} (rowid INTEGER PRIMARY KEY, {keyDefinitions}, UNIQUE ({keyList}));");
        Append(builder, $"CREATE TRIGGER {Quote(insert)} AFTER INSERT ON {source}\nBEGIN\n{WriteKeyed("NEW")}END;");
        Append(builder, $"CREATE TRIGGER {Quote(delete)} AFTER DELETE ON {source}\nBEGIN\n{Forget("OLD")}END;");
        Append(builder, $"CREATE TRIGGER {Quote(update)} AFTER UPDATE OF {watched} ON {source}\nBEGIN\n{Forget("OLD")}{WriteKeyed("NEW")}END;");
        Append(builder, $"INSERT INTO {keys}({keyList}) SELECT {keyList} FROM {source};");
        Append(
            builder,
            $"INSERT INTO {index}(rowid, {columnList}) SELECT k.rowid, {string.Join(", ", columns.Select(c => $"t.{Quote(c)}"))} " +
            $"FROM {source} t JOIN {keys} k ON {string.Join(" AND ", keyColumns.Select(c => $"k.{Quote(c)} = t.{Quote(c)}"))};");
    }

    private static string[] Triggers(string table) =>
        [$"TR_{table}_fts_Insert", $"TR_{table}_fts_Delete", $"TR_{table}_fts_Update"];

    private static string Tokenizer(FullTextTokenizer tokenizer) => tokenizer switch
    {
        FullTextTokenizer.English => "porter unicode61 remove_diacritics 2",
        _ => "unicode61 remove_diacritics 2",
    };

    private static string Values(string row, IEnumerable<string> columns)
        => string.Join(", ", columns.Select(column => $"{row}.{Quote(column)}"));

    private static void Append(MigrationCommandListBuilder builder, string sql)
    {
        builder.AppendLines(sql);
        builder.EndCommand();
    }

    private static string Column(IEntityType entityType, string property, StoreObjectIdentifier store)
    {
        var mapped = entityType.FindProperty(property)
            ?? throw new InvalidOperationException(
                $"'{entityType.DisplayName()}' declares HasFullTextSearch over '{property}', which is not a mapped " +
                "property. Check the name, and that the property is not ignored.");

        return mapped.GetColumnName(store)
            ?? throw new InvalidOperationException(
                $"'{entityType.DisplayName()}.{property}' is not mapped to a column of '{store.Name}', so it cannot " +
                "be indexed for full-text search.");
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string Literal(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static (string Table, string? Schema)? TableOf(MigrationOperation operation) => operation switch
    {
        CreateTableOperation create => (create.Name, create.Schema),
        AlterTableOperation alter => (alter.Name, alter.Schema),
        AddColumnOperation add => (add.Table, add.Schema),
        AlterColumnOperation alter => (alter.Table, alter.Schema),
        DropColumnOperation drop => (drop.Table, drop.Schema),
        RenameColumnOperation rename => (rename.Table, rename.Schema),
        AddUniqueConstraintOperation add => (add.Table, add.Schema),
        DropUniqueConstraintOperation drop => (drop.Table, drop.Schema),
        AddCheckConstraintOperation add => (add.Table, add.Schema),
        DropCheckConstraintOperation drop => (drop.Table, drop.Schema),
        AddPrimaryKeyOperation add => (add.Table, add.Schema),
        DropPrimaryKeyOperation drop => (drop.Table, drop.Schema),
        AddForeignKeyOperation add => (add.Table, add.Schema),
        DropForeignKeyOperation drop => (drop.Table, drop.Schema),
        _ => null,
    };
}
