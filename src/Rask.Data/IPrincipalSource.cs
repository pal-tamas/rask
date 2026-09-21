using System.Security.Claims;

namespace Rask.Data;

/// <summary>
///     Where the data layer learns who is signed in to the work in flight.
/// </summary>
/// <remarks>
///     <para>
///         The seam behind <see cref="Current.Principal" />, <see cref="Current.UserId" /> and the tenant a
///         signed-in user belongs to. Rask.Data declares what it needs and knows nothing about sessions or
///         sign-in, because it does not reference <c>Rask.Core</c> — a package that did would stop working on
///         the front-end lanes.
///     </para>
///     <para>
///         The implementation is registered by the host, is <b>scoped</b>, and reads the principal of the
///         session or request it belongs to. A static read runs outside any DI scope, which is why
///         <see cref="Db.UseScope" /> exists.
///     </para>
///     <para>
///         An explicit <see cref="Tenant.Use" /> or <see cref="Current.UseUser" /> always wins over this: a
///         background job runs for the tenant and user its row recorded, not for whoever happened to enqueue it.
///     </para>
/// </remarks>
public interface IPrincipalSource
{
    /// <summary>The principal of the session or request, or <see langword="null" /> when there is none.</summary>
    ClaimsPrincipal? Current { get; }
}
