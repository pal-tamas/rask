// EF1001: SqliteAnnotationProvider is EF-internal, but it is the provider's own annotation source, and deriving from
// it is the only way to add a table annotation while keeping every one SQLite already reports (autoincrement, SRID,
// …). The base type moves with the EF Core version this package pins; the FullTextSearch migration tests re-verify it.
#pragma warning disable EF1001

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Sqlite.Metadata.Internal;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
/// SQLite's relational annotations, plus the full-text index and the non-overlapping range rule an entity declares,
/// reported on its table.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FullTextSearchBuilderExtensions.HasFullTextSearch{TEntity}"/> stores its spec on the entity type,
/// and EF Core's migrations differ never compares entity-type annotations — only the relational model's. Left there,
/// adding search to an existing table would produce an empty migration, and the index would never be created.
/// Reporting the spec on the <em>table</em> makes adding, changing or removing it an <c>AlterTableOperation</c>
/// like any other, which <see cref="FullTextSearchDdl"/> then turns into the DDL.
/// </para>
/// <para>
/// <see cref="RangeExclusionBuilderExtensions.HasNonOverlappingRange{TEntity}"/> had the same problem and shipped with it:
/// the rule added to an existing table whose columns did not otherwise change produced an empty migration, and
/// the triggers were never created (#1113). It is reported here for the same reason, and
/// <see cref="RangeExclusionDdl"/> reacts to the <c>AlterTableOperation</c>.
/// </para>
/// <para>
/// Registered once, by <c>UseRaskSqlite</c>: EF Core resolves exactly one <see cref="IRelationalAnnotationProvider"/>,
/// so a second replacement would silently win over this one.
/// </para>
/// </remarks>
/// <param name="dependencies">Supplied by EF Core.</param>
internal sealed class RaskSqliteAnnotationProvider(RelationalAnnotationProviderDependencies dependencies)
    : SqliteAnnotationProvider(dependencies)
{
    public override IEnumerable<IAnnotation> For(ITable table, bool designTime)
    {
        foreach (var annotation in base.For(table, designTime))
        {
            yield return annotation;
        }

        // A table shared by several entity types (table splitting) has one of each at most: the first declaration wins,
        // and the DDL reads the same one back from the model.
        foreach (var name in (string[])[FullTextSearchSpec.AnnotationName, RangeExclusionSpec.AnnotationName])
        {
            foreach (var mapping in table.EntityTypeMappings)
            {
                if (mapping.TypeBase.FindAnnotation(name) is { Value: string spec })
                {
                    yield return new Annotation(name, spec);
                    break;
                }
            }
        }

        // JSON indexes travel RESOLVED — name and expression — so a migration that removes one can drop exactly the
        // index the previous one built, whatever became of the property it pointed at (see JsonIndexDdl).
        if (JsonIndexDdl.TableAnnotation(table.EntityTypeMappings.Select(m => m.TypeBase).OfType<IEntityType>(), table.Name)
            is { } indexes)
        {
            yield return new Annotation(JsonIndexSpec.AnnotationName, indexes);
        }
    }
}
