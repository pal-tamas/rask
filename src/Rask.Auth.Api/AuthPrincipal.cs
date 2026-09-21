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

    /// <summary>A principal for <paramref name="user" />, carrying <paramref name="sessionId" /> when there is one.</summary>
    internal static ClaimsPrincipal For(Authenticatable user, Guid? sessionId = null) =>
        For(user.Id, user.Email, user.Roles, sessionId, user.TenantId);

    internal static ClaimsPrincipal For(
        Guid userId, string email, IEnumerable<string> roles, Guid? sessionId, Guid? tenantId = null)
    {
        var identity = new ClaimsIdentity(AuthenticationType, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, email));
        identity.AddClaim(new Claim(ClaimTypes.Email, email));

        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        if (sessionId is { } sid)
        {
            identity.AddClaim(new Claim(SessionClaim, sid.ToString()));
        }

        // The tenant travels ON THE PRINCIPAL, which is what lets a read filter correctly without anything
        // being passed to it: the data layer reads this claim through ITenantSource. An administrator has no
        // tenant and so carries no claim, and every tenant-scoped read then throws until they choose one.
        if (tenantId is { } tenant)
        {
            identity.AddClaim(new Claim(Tenant.ClaimType, tenant.ToString()));
        }

        return new ClaimsPrincipal(identity);
    }

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
