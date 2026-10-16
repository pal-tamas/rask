namespace Rask.Data;

/// <summary>
/// <see cref="PerTenant" /> for a table whose tenants are numbered: <c>PerTenant&lt;int&gt;</c> gives it an
/// <c>int?</c> <c>TenantId</c>, and a <c>TenantId</c> the aggregate declares must have that type.
/// </summary>
/// <typeparam name="TKey"><see cref="Guid" />, <see cref="int" /> or <see cref="long" />.</typeparam>
#pragma warning disable S2326 // the type argument IS the declaration, read by the generator
public abstract class PerTenant<TKey> : IAggregateTrait
    where TKey : struct;
#pragma warning restore S2326
