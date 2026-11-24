using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Rask.Data;

/// <summary>
///     Writes what <see cref="FilteredIndexBuilderExtensions.Where{TEntity}" /> declared as the provider's own index
///     filter, and forgets the declaration — so the model a migration is made from carries the SQL alone.
/// </summary>
/// <param name="sql">The provider's dialect, or null for a provider that writes no SQL.</param>
internal sealed class FilteredIndexConvention(ModelSql? sql) : IModelFinalizingConvention
{
    /// <summary>The provider-neutral declaration: a <see cref="DeclaredExpression" />.</summary>
    internal const string Annotation = "Rask:Filter";

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var index in entityType.GetDeclaredIndexes())
            {
                if (index.FindAnnotation(Annotation)?.Value is not DeclaredExpression filter)
                {
                    continue;
                }

                index.RemoveAnnotation(Annotation);
                if (sql is null)
                {
                    continue;
                }

                sql.Supported(filter.Declaration);
                var rows = ModelExpressionSql.Filter(sql, entityType, filter.Lambda, filter.Declaration);
                index.SetFilter(string.Join(" AND ", [.. KeysNotNull(sql, index, rows), rows]), fromDataAnnotation: true);
            }
        }
    }

    // SQL Server's unique index counts a NULL as a value, so EF Core's own filters NULLs out of it there; the app's
    // filter replaces that one, so it says the same again. PostgreSQL and SQLite never collide on a NULL.
    private static IEnumerable<string> KeysNotNull(ModelSql sql, IConventionIndex index, string rows) =>
        sql.IsSqlServer && index.IsUnique
            ? index.Properties
                .OfType<IConventionProperty>()
                .Where(static p => p.IsNullable)
                .Select(p => $"{sql.Column(p.GetColumnName())} IS NOT NULL")
                .Where(term => !rows.Contains(term, StringComparison.Ordinal))
            : [];
}
