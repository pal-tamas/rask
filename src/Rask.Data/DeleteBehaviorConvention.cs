using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Rask.Data;

/// <summary>What a delete does to every relationship the model left to convention: one rule for children, one for the rest.</summary>
/// <remarks>
///     As the model is finalized, so it lands after Rask's own child rule (a cascade, below an explicit one) and after
///     each entity's <c>Configure</c>: an <c>OnDelete</c> stated there wins. An owned type's key is left alone — EF Core
///     requires it to cascade.
/// </remarks>
internal sealed class DeleteBehaviorConvention(DeleteBehavior children, DeleteBehavior others) : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var key in entityType.GetDeclaredForeignKeys())
            {
                if (key.IsOwnership || key.GetDeleteBehaviorConfigurationSource() == ConfigurationSource.Explicit)
                {
                    continue;
                }

                key.SetDeleteBehavior(ModelBuilderExtensions.IsChildKey(key) ? children : others, fromDataAnnotation: true);
            }
        }
    }
}
