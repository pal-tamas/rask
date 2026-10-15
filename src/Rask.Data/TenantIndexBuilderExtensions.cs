using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>Says that an index of a <see cref="Tenancy.PerTenant" /> table holds across every tenant.</summary>
/// <remarks>
///     <code>
/// builder.HasIndex(u =&gt; u.Username)
///     .HasDatabaseName("User_username_key")
///     .IsUnique("This username is taken.")
///     .AcrossTenants();
///     </code>
///     <para>
///         Rask puts <c>TenantId</c> in front of every other index of such a table, so uniqueness means "within
///         this tenant". Some keys are global by nature — a sign-in name, an external reference, a chip id — and
///         this one is left exactly as written. Its unique rule is then asked about every tenant's rows too: a
///         value another tenant holds is refused at the field and at the save, with the index's message.
///     </para>
/// </remarks>
public static class TenantIndexBuilderExtensions
{
    /// <summary>Leaves the index as written: it holds across every tenant, not within one.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The same builder.</returns>
    public static IndexBuilder AcrossTenants(this IndexBuilder index)
    {
        ArgumentNullException.ThrowIfNull(index);

        index.Metadata.SetAnnotation(TenantIndexes.AcrossAnnotation, true);
        return index;
    }

    /// <summary>Leaves the index as written: it holds across every tenant, not within one.</summary>
    /// <typeparam name="TEntity">The entity the index is on.</typeparam>
    /// <param name="index">The index.</param>
    /// <returns>The same builder.</returns>
    public static IndexBuilder<TEntity> AcrossTenants<TEntity>(this IndexBuilder<TEntity> index)
    {
        AcrossTenants((IndexBuilder)index);
        return index;
    }
}
