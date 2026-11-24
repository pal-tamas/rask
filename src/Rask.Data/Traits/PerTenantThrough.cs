namespace Rask.Data;

/// <summary>
///     The aggregate belongs to a tenant through another one it points at, and keeps no <c>TenantId</c> of its own:
///     <c>Distribution : Aggregate&lt;DistributionId, PerTenantThrough&lt;Supply&gt;&gt;</c> is the tenant's whose
///     <c>Supply</c> it names.
/// </summary>
/// <remarks>
///     <para>
///         Its filter asks the parent, as a child's does: a row is read in the tenant that can read the
///         <typeparamref name="TParent" /> its id names, and a save under a parent of another tenant is refused. A row
///         whose id is NULL belongs to nobody, and only <c>Across(reason)</c> or <see cref="Tenant.Across" /> reads it.
///     </para>
///     <para>
///         <typeparamref name="TParent" /> is a <see cref="PerTenant" /> aggregate, and this one holds its id — a
///         strongly-typed id, or a foreign key its <c>Configure</c> maps.
///     </para>
/// </remarks>
/// <typeparam name="TParent">The aggregate whose tenant this one's rows are.</typeparam>
#pragma warning disable S2326 // the type argument IS the declaration, read by the generator
public abstract class PerTenantThrough<TParent> : IAggregateTrait
    where TParent : class, IAggregate;
#pragma warning restore S2326
