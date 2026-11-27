namespace Rask.Data;

/// <summary>
/// <see cref="PerTenant" /> for a table whose tenants are numbered: <c>PerTenant&lt;int&gt;</c> gives it an
/// <c>int?</c> <c>TenantId</c>, and a <c>TenantId</c> the aggregate declares must have that type.
/// </summary>
/// <remarks>
/// The key may also be the tenant's own typed id over one of them — <c>PerTenant&lt;TenantId&gt;</c> for a tenant
/// keyed by <c>public readonly partial record struct TenantId(Guid Value);</c> — and the table then keeps a
/// <c>TenantId?</c>, filtered by and stamped with the tenant in flight like any other column. With a typed tenant, every
/// tenant-scoped table keeps that id: a plain <see cref="PerTenant" /> (a <c>Guid?</c>) beside it is RASK114.
/// </remarks>
/// <typeparam name="TKey">
/// <see cref="Guid" />, <see cref="int" />, <see cref="long" />, or the tenant's typed id holding one of them.
/// </typeparam>
#pragma warning disable S2326 // the type argument IS the declaration, read by the generator
public abstract class PerTenant<TKey> : IAggregateTrait
    where TKey : struct;
#pragma warning restore S2326
