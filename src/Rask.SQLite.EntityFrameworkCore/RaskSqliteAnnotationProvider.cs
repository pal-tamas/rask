// EF1001: SqliteAnnotationProvider is EF-internal, but it is the provider's own annotation source, and deriving from
// it is the only way to add a table annotation while keeping every one SQLite already reports (autoincrement, SRID,
// …). The base type moves with the EF Core version this package pins; the FullTextSearch migration tests re-verify it.
#pragma warning disable EF1001

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Sqlite.Metadata.Internal;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
/// SQLite's relational annotations, plus the full-text index an entity declares, reported on its table.
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

        // A table shared by several entity types (table splitting) has one index at most: the first declaration wins,
        // and the DDL reads the same one back from the model.
        foreach (var mapping in table.EntityTypeMappings)
        {
            if (mapping.TypeBase.FindAnnotation(FullTextSearchSpec.AnnotationName) is { Value: string spec })
            {
                yield return new Annotation(FullTextSearchSpec.AnnotationName, spec);
                yield break;
            }
        }
    }
}
