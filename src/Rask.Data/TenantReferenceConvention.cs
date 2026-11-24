using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Rask.Data;

/// <summary>
///     Declares <see cref="TenantReference" />s for the tenant <c>TenantReferences&lt;T&gt;()</c> names: the tenant's
///     name on the model as it is initialized, the references as it is finalized.
/// </summary>
/// <param name="tenant">The tenant entity type's name.</param>
internal sealed class TenantReferenceConvention(string tenant) : IModelInitializedConvention, IModelFinalizingConvention
{
    public void ProcessModelInitialized(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context) =>
        modelBuilder.HasAnnotation(TenantReference.TenantAnnotation, tenant);

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        TenantReferenceDeclaration.Declare(modelBuilder, name: null);
        modelBuilder.HasNoAnnotation(TenantReference.TenantAnnotation);
    }
}
