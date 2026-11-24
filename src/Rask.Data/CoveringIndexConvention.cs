using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Rask.Data;

/// <summary>
///     Writes what <see cref="CoveringIndexBuilderExtensions.Covering(IndexBuilder, string[])" /> declared as the
///     provider's own included columns, and forgets the declaration — so the model a migration is made from carries
///     the provider's annotation alone, and on SQLite nothing.
/// </summary>
/// <param name="providerName">The EF Core provider the model is built for, or null when there is none.</param>
internal sealed class CoveringIndexConvention(string? providerName) : IModelFinalizingConvention
{
    /// <summary>The provider-neutral declaration: the covered properties' names.</summary>
    internal const string Annotation = "Rask:Covering";

    // EF Core's SqlServerAnnotationNames.Include and Npgsql's NpgsqlAnnotationNames.IndexInclude: both hold property
    // paths (`Amount`, `Km.Start`), which the provider maps to columns as it writes the index. Named here, since
    // Rask.Data references neither.
    internal const string SqlServerInclude = "SqlServer:Include";

    internal const string PostgresInclude = "Npgsql:IndexInclude";

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var include = providerName switch
        {
            ProviderNames.SqlServer => SqlServerInclude,
            ProviderNames.Postgres => PostgresInclude,
            _ => null,
        };

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var index in entityType.GetDeclaredIndexes())
            {
                if (index.FindAnnotation(Annotation)?.Value is not string[] covered)
                {
                    continue;
                }

                var columns = Columns(entityType, index, covered);
                index.RemoveAnnotation(Annotation);
                if (include is not null)
                {
                    var stated = index.FindAnnotation(include)?.Value as string[] ?? [];
                    index.SetAnnotation(include, stated.Union(columns, StringComparer.Ordinal).ToArray());
                }
            }
        }
    }

    // Each covered member as the paths of the columns it is: a property is its own name, and a value object of several
    // members stored in columns of its own is each of them — `Km` is `Km.Start` and `Km.End`, which both providers'
    // INCLUDE resolve to the columns, however those are named.
    private static List<string> Columns(IConventionEntityType entityType, IConventionIndex index, string[] covered)
    {
        var columns = new List<string>();
        foreach (var name in covered)
        {
            var paths = PathsOf(entityType, name) ?? throw new InvalidOperationException(
                $"The index on {entityType.DisplayName()}({Keys(index)}) covers {name}, which is not one of its mapped "
                + "properties. Cover a column the entity maps — Covering(x => new { x.Amount }) — or a value object it stores "
                + "in columns of its own, and leave a navigation, a value object stored as JSON or an ignored member out.");

            foreach (var path in paths)
            {
                if (index.Properties.Any(p => string.Equals(p.Name, path, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        $"The index on {entityType.DisplayName()}({Keys(index)}) covers {name}, which is already one of its keys. "
                        + $"Remove {name} from Covering(…): a key column is in the index already.");
                }

                columns.Add(path);
            }
        }

        return columns;
    }

    // `Amount`, `Km` or `Km.Start`; null when the path names nothing the entity stores in columns of its table.
    private static List<string>? PathsOf(IConventionEntityType entityType, string name)
    {
        IConventionTypeBase type = entityType;
        var steps = name.Split('.');
        foreach (var step in steps[..^1])
        {
            if (type.FindComplexProperty(step) is not { IsCollection: false } held || held.ComplexType.IsMappedToJson())
            {
                return null;
            }

            type = held.ComplexType;
        }

        if (type.FindProperty(steps[^1]) is not null)
        {
            return [name];
        }

        return type.FindComplexProperty(steps[^1]) is { IsCollection: false } complex && !complex.ComplexType.IsMappedToJson()
            ? [.. Leaves(complex.ComplexType, name)]
            : null;
    }

    private static IEnumerable<string> Leaves(IConventionComplexType type, string path) =>
        type.GetProperties().Select(p => path + "." + p.Name)
            .Concat(type.GetComplexProperties().Where(static c => !c.IsCollection).SelectMany(c => Leaves(c.ComplexType, path + "." + c.Name)));

    private static string Keys(IConventionIndex index) => string.Join(", ", index.Properties.Select(p => p.Name));
}
