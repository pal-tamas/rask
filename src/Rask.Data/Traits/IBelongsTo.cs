namespace Rask.Data;

/// <summary>
///     The aggregate belongs to a tenant: a <c>TenantId</c>, a query filter, and the tenant stamped on add —
///     <c>Company : Aggregate&lt;CompanyId&gt;, IBelongsTo&lt;Tenant&gt;</c>.
/// </summary>
/// <remarks>
///     <para>
///         <typeparamref name="TTenant" /> is the app's tenant aggregate, the one that says <see cref="ITenant" />, and
///         the table keeps the tenant in that aggregate's key: a <c>Guid?</c> for a <see cref="Guid" /> or a
///         <see cref="string" /> tenant, an <c>int?</c> or a <c>long?</c> for a numbered one, the tenant's own typed id
///         for a typed one (<c>TenantId? TenantId</c>). A <c>TenantId</c> the aggregate declares must have that type, and
///         one it leaves out is a shadow column of it.
///     </para>
///     <para>
///         The table's <c>TenantId</c> is a foreign key to the tenant's own key, so the database refuses to delete a
///         tenant that still has rows. A child needs nothing: it belongs to its root's tenant.
///     </para>
/// </remarks>
/// <typeparam name="TTenant">The app's tenant aggregate.</typeparam>
#pragma warning disable CA1040 // a marker: the interface IS the declaration, read by the generator
#pragma warning disable S2326 // the type argument IS the declaration, read by the generator
public interface IBelongsTo<TTenant>
    where TTenant : class, IAggregate;
#pragma warning restore S2326
#pragma warning restore CA1040
