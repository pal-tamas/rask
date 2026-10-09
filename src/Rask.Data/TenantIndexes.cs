using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Rask.Data;

/// <summary>
///     Makes the indexes of a <see cref="Tenancy.PerTenant" /> table hold within a tenant.
/// </summary>
/// <remarks>
///     <para>
///         An index that does not name the tenant gains <c>TenantId</c> at the FRONT: uniqueness then means
///         "within this tenant", and the filtered query can use the index rather than scanning and discarding.
///         One that names it ANYWHERE is left exactly as declared — the author already said where the tenant
///         goes, and a table that exists has its indexes in the order it has them.
///     </para>
///     <para>
///         It runs three times, because an index can be declared at three moments and one left unprefixed is
///         unique across every tenant (#1233): with the conventions, for what was declared before them; after
///         each entity's own <c>Configure</c>, which <see cref="ModelRegistry" /> runs later; and as the model
///         is finalized, because EF Core puts an <c>[Index]</c> attribute's index back after it is replaced.
///         Running again is safe — an index it already rewrote names the tenant.
///     </para>
/// </remarks>
internal static class TenantIndexes
{
    /// <summary>Prefixes the indexes of every tenant-scoped entity in <paramref name="model" />.</summary>
    internal static void Prefix(IMutableModel model)
    {
        foreach (var entityType in model.GetEntityTypes().ToList())
        {
            if (typeof(IEntity).IsAssignableFrom(entityType.ClrType) &&
                ConventionRegistry.ScopeFor(entityType.ClrType) == Tenancy.PerTenant)
            {
                Prefix(entityType);
            }
        }
    }

    /// <summary>Prefixes the indexes of <paramref name="entityType" />, which must map its tenant column.</summary>
    internal static void Prefix(IMutableEntityType entityType)
    {
        if (entityType.FindProperty(Columns.TenantId) is not { } tenant)
        {
            return;
        }

        foreach (var index in entityType.GetIndexes().ToList())
        {
            if (index.Properties.Contains(tenant))
            {
                continue;
            }

            IReadOnlyList<IMutableProperty> prefixed = [tenant, .. index.Properties];
            var replacement = entityType.FindIndex(prefixed) ?? entityType.AddIndex(prefixed);

            if (index.IsUnique)
            {
                replacement.IsUnique = true;
            }

            // What the index was told about itself travels with it: the message a violation is reported with.
            if (index.FindAnnotation(UniqueViolation.Annotation)?.Value is { } message)
            {
                replacement.SetAnnotation(UniqueViolation.Annotation, message);
            }

            entityType.RemoveIndex(index);
        }
    }

    /// <summary>The last of the three passes: after EF Core's own conventions have finished adding indexes.</summary>
    internal sealed class Convention : IModelFinalizingConvention
    {
        /// <inheritdoc />
        public void ProcessModelFinalizing(
            IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);
            Prefix((IMutableModel)modelBuilder.Metadata);
        }
    }
}
