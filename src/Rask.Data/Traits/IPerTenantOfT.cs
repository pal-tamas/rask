namespace Rask.Data;

/// <summary>
/// <see cref="IPerTenant" /> for a table whose tenants are numbered: <c>IPerTenant&lt;int&gt;</c> gives it an
/// <c>int?</c> <c>TenantId</c>, and a <c>TenantId</c> the aggregate declares must have that type.
/// </summary>
/// <typeparam name="TKey"><see cref="Guid" />, <see cref="int" /> or <see cref="long" />.</typeparam>
#pragma warning disable CA1040, S2326 // a marker: the type argument IS the declaration, read by the generator
public interface IPerTenant<TKey>
    where TKey : struct;
#pragma warning restore CA1040, S2326
