using System.Security.Claims;
using Rask.Auth;

namespace Rask.Server;

/// <summary>Where <c>MapRaskTenantChoice</c> answers, and who may choose there.</summary>
public sealed class TenantChoiceOptions
{
    /// <summary>The path the choice is posted to. <c>/api/tenant</c> by default.</summary>
    public string Path { get; set; } = "/api/tenant";

    /// <summary>
    /// The role that may choose a tenant to act in, asked of the signed-in user with <c>IsInRole</c>.
    /// <see cref="RaskRoles.Admin" /> by default; <see langword="null" /> turns choosing off.
    /// </summary>
    /// <remarks>
    /// Read from the principal your sign-in builds, at every choice. A token that names its administrator another way
    /// (an <c>isAdmin</c> claim) adds a role claim for this, as ASP.NET's <c>[Authorize(Roles = …)]</c> would need.
    /// </remarks>
    public string? Role { get; set; } = RaskRoles.Admin;

    /// <summary>
    /// Whether the signed-in user belongs to a tenant of their own, and so is refused whatever role they hold:
    /// somebody signed in to one customer never acts in another. Nobody does by default.
    /// </summary>
    /// <remarks>
    /// Answer from what the sign-in says about the <b>account</b>, not the tenant the user acts in now: once an
    /// administrator has chosen, the principal may carry the chosen tenant too, and they must still be able to choose
    /// again or leave.
    /// </remarks>
    public Func<ClaimsPrincipal, bool> BelongsToTenant { get; set; } = static _ => false;
}
