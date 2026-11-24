using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Rask.Data;

/// <summary>
///     Writes what <see cref="ComputedColumnBuilderExtensions.Computed{TEntity, TProperty}" /> declared as the
///     provider's own stored computed column, and forgets the declaration — so the model a migration is made from
///     carries the SQL alone.
/// </summary>
/// <param name="sql">The provider's dialect, or null for a provider that writes no SQL.</param>
internal sealed class ComputedColumnConvention(ModelSql? sql) : IModelFinalizingConvention
{
    /// <summary>The provider-neutral declaration: a <see cref="DeclaredExpression" />.</summary>
    internal const string Annotation = "Rask:Computed";

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var property in entityType.GetDeclaredProperties())
            {
                if (property.FindAnnotation(Annotation)?.Value is not DeclaredExpression computed)
                {
                    continue;
                }

                property.RemoveAnnotation(Annotation);
                if (sql is null)
                {
                    continue;
                }

                sql.Supported(computed.Declaration);
                property.SetComputedColumnSql(
                    ModelExpressionSql.Value(sql, entityType, computed.Lambda, computed.Declaration), fromDataAnnotation: true);
                property.SetIsStored(true, fromDataAnnotation: true);
            }
        }
    }
}
