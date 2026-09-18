namespace Rask.Data;

/// <summary>
///     Whether a table is partitioned by tenant, as its <c>Scope</c> const declares it.
/// </summary>
/// <remarks>
///     <para>
///         Opt-IN, like every other member of the const family: a table that says nothing is one table for
///         everybody, exactly as it is today. Declaring <see cref="PerTenant" /> adds a <c>TenantId</c>
///         column, a query filter that no read can compose away, and a <c>TenantId</c> prefix on every index.
///     </para>
///     <code>
/// public sealed class Invoice : Aggregate&lt;Guid&gt;
/// {
///     public const Tenancy Scope = Tenancy.PerTenant;
/// }
///     </code>
///     <para>
///         A child entity takes its aggregate root's answer: it is part of that aggregate, so it belongs to
///         whichever tenant the root does, and it carries its own <c>TenantId</c> so its own read face cannot
///         return another tenant's rows.
///     </para>
/// </remarks>
public enum Tenancy
{
    /// <summary>One table for everybody. The default, and what a table that declares nothing gets.</summary>
    Shared = 0,

    /// <summary>Partitioned by tenant: a <c>TenantId</c> column, a query filter, and tenant-prefixed indexes.</summary>
    PerTenant = 1,
}
