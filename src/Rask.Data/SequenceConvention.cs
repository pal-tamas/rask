using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Rask.Data;

/// <summary>
///     Declares each <see cref="SequenceBuilderExtensions.FromSequence(PropertyBuilder, string?, long)" /> as the
///     provider's own sequence — on SQL Server and PostgreSQL, where a migration makes it — and forgets the declaration.
/// </summary>
/// <param name="sql">The provider's dialect, or null for a provider that writes no SQL.</param>
/// <remarks>
///     SQLite has no sequence: its value generator keeps one in a table of its own, made the first time it is asked.
/// </remarks>
internal sealed class SequenceConvention(ModelSql? sql) : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var property in modelBuilder.Metadata.GetEntityTypes().SelectMany(static e => e.GetDeclaredProperties()).ToList())
        {
            if (property.FindAnnotation(SequenceBuilderExtensions.Annotation)?.Value is not string name)
            {
                continue;
            }

            var start = property.FindAnnotation(SequenceBuilderExtensions.StartAnnotation)?.Value as long? ?? 1;
            property.RemoveAnnotation(SequenceBuilderExtensions.Annotation);
            property.RemoveAnnotation(SequenceBuilderExtensions.StartAnnotation);

            if (sql is { Provider: ProviderNames.SqlServer or ProviderNames.Postgres })
            {
                modelBuilder.HasSequence(name, modelBuilder.Metadata.GetDefaultSchema(), fromDataAnnotation: true)?.StartsAt(start, fromDataAnnotation: true);
            }
        }
    }
}
