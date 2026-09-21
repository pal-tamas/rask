using Rask.Core.Authentication;
using Rask.Data;

namespace Rask;

/// <summary>
///     The tenant of the signed-in user, read off the claim their principal carries.
/// </summary>
/// <remarks>
///     <para>
///         The tenant is an outcome of authentication, not an input to routing: sign-in finds the user, and
///         the user says which tenant they belong to. So it travels on the <c>ClaimsPrincipal</c> and is read
///         back here, which is what lets <c>Invoice.Read.Where(…)</c> filter correctly with nothing passed
///         to it.
///     </para>
///     <para>
///         Scoped, because the principal is: the provider resolved here belongs to one session, and
///         <c>Db.UseScope</c> is what makes that session's provider reachable from a read that runs outside
///         any DI scope.
///     </para>
///     <para>
///         An administrator belongs to no tenant and so carries no claim. This answers null for them, and a
///         tenant-scoped read then throws until they choose a tenant to work in — which is the intended
///         behaviour, not an oversight: an admin should say which tenant they are acting in, not silently
///         read across all of them.
///     </para>
///     <para>
///         Lives in the meta package because it is the only assembly that can see both sides:
///         <c>IUserProvider</c> is Rask.Core's and <c>ITenantSource</c> is Rask.Data's, and neither of those
///         references the other.
///     </para>
/// </remarks>
internal sealed class ClaimsTenantSource(IUserProvider users) : ITenantSource
{
    /// <inheritdoc />
    public Guid? Current =>
        Guid.TryParse(users.Current?.FindFirst(Tenant.ClaimType)?.Value, out var tenant) ? tenant : null;
}
