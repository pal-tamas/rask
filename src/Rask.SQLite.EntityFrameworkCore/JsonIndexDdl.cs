using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
/// Builds the SQLite expression indexes <see cref="JsonIndexBuilderExtensions.HasJsonIndex{TEntity}"/> declares (#1112).
/// </summary>
/// <remarks>
/// <para>
/// SQLite uses an expression index only for a query expression that is the SAME expression, so each index is
/// written the way EF Core's SQLite provider writes the filter: <c>"Meta" -&gt;&gt; 'Status'</c> for a value at the
/// top of the JSON, <c>"Meta" -&gt;&gt; '$.Address.City'</c> for one deeper. The two spellings are EF's, not a choice
/// made here — a single <c>json_extract("Meta", '$.Status')</c> form would build an index no query ever uses, which
/// is why the tests assert the query plan rather than the DDL.
/// </para>
/// <para>
/// The table carries the RESOLVED indexes — name and expression — as its annotation (see
/// <see cref="RaskSqliteAnnotationProvider"/>), so removing a declaration drops exactly the index the previous
/// migration built, even when the property it pointed at is gone from the model too.
/// </para>
/// </remarks>
internal static class JsonIndexDdl
{
    private const char EntrySeparator = '\u001e';
    private const char FieldSeparator = '\u001f';

    /// <summary>The annotation value the table carries: every resolved index, name and expression.</summary>
    public static string? TableAnnotation(IEntityType entityType, string table) => TableAnnotation([entityType], table);

    /// <summary>
    /// The same over every entity type mapped to the table — a hierarchy, or table splitting — so an index one of them
    /// declares is dropped when that declaration goes, not only the first type's.
    /// </summary>
    public static string? TableAnnotation(IEnumerable<IEntityType> entityTypes, string table)
    {
        var indexes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var entityType in entityTypes)
        {
            if (!JsonIndexSpec.TryParse(entityType.FindAnnotation(JsonIndexSpec.AnnotationName)?.Value, out var spec))
            {
                continue;
            }

            foreach (var path in spec.Paths)
            {
                var (name, expression) = Resolve(entityType, table, path);
                indexes[name] = expression;
            }
        }

        return indexes.Count == 0
            ? null
            : string.Join(EntrySeparator, indexes.Select(i => i.Key + FieldSeparator + i.Value));
    }

    public static IReadOnlyList<MigrationCommand> Build(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model,
        MigrationsSqlGeneratorDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(dependencies);

        var builder = new MigrationCommandListBuilder(dependencies);
        var emitted = false;

        // A declaration removed or changed: drop each index the table had that it no longer has.
        foreach (var alter in operations.OfType<AlterTableOperation>())
        {
            var was = Parse(alter.OldTable[JsonIndexSpec.AnnotationName] as string);
            var now = Parse(alter[JsonIndexSpec.AnnotationName] as string).Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var gone in was.Where(i => !now.Contains(i.Name)))
            {
                Append(builder, $"DROP INDEX IF EXISTS {Quote(gone.Name)};");
                emitted = true;
            }
        }

        if (model is null)
        {
            return emitted ? builder.GetCommandList() : [];
        }

        var dropped = operations.OfType<DropTableOperation>().Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
        var touched = operations.Select(RangeExclusionDdl.TableOf).OfType<string>().ToHashSet(StringComparer.Ordinal);

        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.GetTableName() is not { } table || dropped.Contains(table) || !touched.Contains(table)
                || TableAnnotation(entityType, table) is not { } resolved)
            {
                continue;
            }

            // An index keeps its name across ALTER TABLE ... RENAME, so the one built under the old name would sit
            // beside the one built below. The declaration is the same one, so its old names are this one's with the
            // old table in them.
            foreach (var rename in operations.OfType<RenameTableOperation>().Where(r => (r.NewName ?? r.Name) == table))
            {
                foreach (var (name, _) in Parse(TableAnnotation(entityType, rename.Name)))
                {
                    Append(builder, $"DROP INDEX IF EXISTS {Quote(name)};");
                }
            }

            // IF NOT EXISTS: a migration that only touches the table must not fail on an index that is already
            // there, and one that REBUILT the table (SQLite's way to alter most things) dropped it with the old one.
            foreach (var (name, expression) in Parse(resolved))
            {
                Append(builder, $"CREATE INDEX IF NOT EXISTS {Quote(name)} ON {Quote(table)} ({expression});");
            }

            emitted = true;
        }

        return emitted ? builder.GetCommandList() : [];
    }

    private static (string Name, string Expression) Resolve(IEntityType entityType, string table, IReadOnlyList<string> path)
    {
        var navigation = entityType.FindNavigation(path[0])
            ?? throw Unresolvable(entityType, path, $"'{path[0]}' is not a navigation of '{entityType.DisplayName()}'");
        var owned = navigation.TargetEntityType;
        var column = owned.GetContainerColumnName()
            ?? throw Unresolvable(entityType, path, $"'{path[0]}' is not mapped to a JSON column — map it with ToJson()");

        var jsonNames = new List<string>();
        var current = owned;
        for (var i = 1; i < path.Count; i++)
        {
            if (i < path.Count - 1)
            {
                var inner = current.FindNavigation(path[i])
                    ?? throw Unresolvable(entityType, path, $"'{path[i]}' is not a navigation of '{current.DisplayName()}'");
                current = inner.TargetEntityType;
                jsonNames.Add(current.GetJsonPropertyName() ?? path[i]);
            }
            else
            {
                var property = current.FindProperty(path[i])
                    ?? throw Unresolvable(entityType, path, $"'{path[i]}' is not a property of '{current.DisplayName()}'");
                jsonNames.Add(property.GetJsonPropertyName() ?? path[i]);
            }
        }

        foreach (var name in jsonNames.Where(n => !n.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')))
        {
            // EF quotes such a name inside the JSON path, and the index would have to match that spelling exactly.
            throw Unresolvable(entityType, path, $"the JSON property name '{name}' is not a plain identifier");
        }

        var jsonPath = jsonNames.Count == 1 ? jsonNames[0] : "$." + string.Join('.', jsonNames);
        // Segments joined with '.', which a JSON name here cannot contain: joined with '_', Meta.Address_City and
        // Meta.Address.City were one name for two indexes, and IF NOT EXISTS then skipped the second.
        return (
            string.Create(CultureInfo.InvariantCulture, $"IX_{table}_{column}.{string.Join('.', jsonNames)}"),
            $"{Quote(column)} ->> '{jsonPath}'");
    }

    private static InvalidOperationException Unresolvable(IEntityType entityType, IReadOnlyList<string> path, string why) =>
        new($"HasJsonIndex(p => p.{string.Join('.', path)}) on '{entityType.DisplayName()}' cannot be indexed: {why}.");

    private static List<(string Name, string Expression)> Parse(string? annotation) =>
        string.IsNullOrEmpty(annotation)
            ? []
            : annotation.Split(EntrySeparator).Select(e => e.Split(FieldSeparator)).Select(p => (p[0], p[1])).ToList();

    private static void Append(MigrationCommandListBuilder builder, string sql)
    {
        builder.AppendLines(sql);
        builder.EndCommand();
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
