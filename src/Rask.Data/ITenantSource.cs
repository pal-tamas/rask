namespace Rask.Data;

/// <summary>
///     Where a context learns which tenant the signed-in user belongs to.
/// </summary>
/// <remarks>
///     <para>
///         The tenant travels as a claim on the <c>ClaimsPrincipal</c>, so the answer lives with
///         authentication rather than with the data layer. This interface is the seam: Rask.Data declares
///         what it needs and knows nothing about principals, claims or sign-in, because it does not
///         reference <c>Rask.Core</c> — a package that did would stop working on the front-end lanes.
///     </para>
///     <para>
///         The implementation is registered by the host, is <b>scoped</b>, and reads the principal of the
///         session it belongs to. A context built outside a scope has none, which is why
///         <see cref="Db.UseScope" /> exists.
///     </para>
///     <para>
///         An explicit <see cref="Tenant.Use" /> or <see cref="Tenant.Across" /> always wins over this: a
///         background job runs for the tenant its row recorded, not for whoever happened to enqueue it.
///     </para>
/// </remarks>
public interface ITenantSource
{
    /// <summary>The tenant the current principal belongs to, or <see langword="null" /> when there is none.</summary>
    Guid? Current { get; }
}
