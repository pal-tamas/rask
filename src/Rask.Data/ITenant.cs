namespace Rask.Data;

/// <summary>
///     Says which of the app's aggregates is its tenant: the row <c>Current.Tenant</c> returns.
/// </summary>
/// <remarks>
///     <para>
///         Nothing to implement — the interface is the declaration, read by the build:
///         <code>
/// public sealed class Tenant : Aggregate&lt;int, Deletable&gt;, ITenant { … }
///         </code>
///         and from then on <c>Current.Tenant</c> is that tenant's read face (<c>TenantRead?</c>), with
///         <c>Current.Tenant.Id</c> in the aggregate's own key type, and <c>Current.RequiredTenant</c> the same row
///         or a thrown exception when the work belongs to no tenant.
///     </para>
///     <para>
///         One aggregate per app says it, and its key is a <see cref="Guid" />, an <see cref="int" />, a
///         <see cref="long" /> or a <see cref="string" /> holding a <see cref="Guid" /> — the one its
///         <see cref="PerTenant" /> tables keep in <c>TenantId</c>. The build refuses anything else.
///     </para>
///     <para>
///         The row is read through a cache the framework fills as each request, live event and background job
///         starts, so reading it is not a query; a save of the tenant drops what the cache held. To change it, load
///         the aggregate — <c>var tenant = await Tenant.Find(Current.RequiredTenant.Id);</c> — and save that.
///     </para>
/// </remarks>
public interface ITenant;
