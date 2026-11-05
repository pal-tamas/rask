using System.Security.Claims;
using Rask.Data;

namespace Rask.Auth;

/// <summary>The claims a signed-in user carries, built in one place.</summary>
internal static class AuthPrincipal
{
    /// <summary>The claim naming the session row a cookie or bearer token belongs to.</summary>
    internal const string SessionClaim = "sid";

    /// <summary>The authentication type of every principal Rask.Auth issues.</summary>
    internal const string AuthenticationType = "Rask.Auth";

    /// <summary>
    /// A principal for <paramref name="user" />, named <paramref name="name" /> — what it signs in with — and carrying
    /// <paramref name="sessionId" /> when there is one.
    /// </summary>
    internal static ClaimsPrincipal For<TKey>(Authenticatable<TKey> user, string name, Guid? sessionId = null)
        where TKey : notnull =>
        For(user.AccountId, name, user.Email, user.Roles, sessionId, user.TenantId, user.OwnClaims());

    /// <summary>A principal from the parts of an account that reach it.</summary>
    /// <param name="userId">The account, as the Guid Rask carries it.</param>
    /// <param name="name">What the account signs in with: its address, or its user name.</param>
    /// <param name="email">Its address; empty for an account that has none, which then carries no email claim.</param>
    /// <param name="roles">Its roles.</param>
    /// <param name="sessionId">The session row, when there is one.</param>
    /// <param name="tenantId">The tenant it is in, when it is in one.</param>
    /// <param name="ownClaims">The claims the account gives for itself.</param>
    internal static ClaimsPrincipal For(
        Guid userId, string name, string email, IEnumerable<string> roles, Guid? sessionId, Guid? tenantId = null,
        IReadOnlyList<OwnClaim>? ownClaims = null)
    {
        var identity = new ClaimsIdentity(AuthenticationType, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, name));

        if (email.Length > 0)
        {
            identity.AddClaim(new Claim(ClaimTypes.Email, email));
        }

        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        if (sessionId is { } sid)
        {
            identity.AddClaim(new Claim(SessionClaim, sid.ToString()));
        }

        // The tenant travels ON THE PRINCIPAL, which is what lets a read filter correctly without anything
        // being passed to it: the data layer reads this claim through IPrincipalSource. An administrator has no
        // tenant and so carries no claim, and every tenant-scoped read then throws until they choose one.
        if (tenantId is { } tenant)
        {
            identity.AddClaim(new Claim(Tenant.ClaimType, tenant.ToString()));
        }

        foreach (var own in ownClaims ?? [])
        {
            identity.AddClaim(new Claim(own.Type, own.Value));
        }

        return new ClaimsPrincipal(identity);
    }

    /// <summary>Whether <paramref name="claimType" /> is one Rask.Auth issues itself, and so not an account's to give.</summary>
    internal static bool Issues(string claimType) =>
        claimType is ClaimTypes.NameIdentifier or ClaimTypes.Name or ClaimTypes.Email or ClaimTypes.Role
            or SessionClaim or Tenant.ClaimType;

    /// <summary>The tenant a principal belongs to, if any.</summary>
    internal static Guid? TenantId(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirst(Tenant.ClaimType)?.Value, out var id) ? id : null;

    /// <summary>The session id a principal carries, if any.</summary>
    internal static Guid? SessionId(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirst(SessionClaim)?.Value, out var id) ? id : null;

    /// <summary>The user id a principal carries, if any.</summary>
    internal static Guid? UserId(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
}
