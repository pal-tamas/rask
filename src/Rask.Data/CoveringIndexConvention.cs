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
    // names, which the provider maps to columns as it writes the index. Named here, since Rask.Data references neither.
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

                Check(entityType, index, covered);
                index.RemoveAnnotation(Annotation);
                if (include is not null)
                {
                    var stated = index.FindAnnotation(include)?.Value as string[] ?? [];
                    index.SetAnnotation(include, stated.Union(covered, StringComparer.Ordinal).ToArray());
                }
            }
        }
    }

    private static void Check(IConventionEntityType entityType, IConventionIndex index, string[] covered)
    {
        foreach (var name in covered)
        {
            if (entityType.FindProperty(name) is null)
            {
                throw new InvalidOperationException(
                    $"The index on {entityType.DisplayName()}({Keys(index)}) covers {name}, which is not one of its mapped "
                    + "properties. Cover a column the entity maps — Covering(x => new { x.Amount }) — and leave a navigation, a "
                    + "complex property or an ignored member out.");
            }

            if (index.Properties.Any(p => string.Equals(p.Name, name, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"The index on {entityType.DisplayName()}({Keys(index)}) covers {name}, which is already one of its keys. "
                    + $"Remove {name} from Covering(…): a key column is in the index already.");
            }
        }
    }

    private static string Keys(IConventionIndex index) => string.Join(", ", index.Properties.Select(p => p.Name));
}
