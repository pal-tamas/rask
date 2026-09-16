using System.Security.Claims;

namespace Rask.Core.Authentication;

/// <summary>Reads a signed-in user's id off a principal.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in user's id, or <see langword="null" /> when nobody is signed in.</summary>
    /// <param name="principal">The principal, such as <c>IUserProvider.Current</c>.</param>
    /// <returns>The id of the app's <c>User</c>, ready for <c>User.FindAsync(id)</c>.</returns>
    /// <example>
    /// <code>
    /// var me = users.Current.UserId() is { } id ? await User.FindAsync(id, CancellationToken) : null;
    /// </code>
    /// </example>
    public static Guid? UserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.Identity?.IsAuthenticated == true
               && Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : null;
    }

    /// <summary>The id of the session this principal signed in with, or <see langword="null" /> when there is none.</summary>
    /// <param name="principal">The principal, such as <c>IUserProvider.Current</c>.</param>
    /// <returns>The id of the <c>Session</c> row, so a device list can mark "this device".</returns>
    public static Guid? SessionId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.Identity?.IsAuthenticated == true
               && Guid.TryParse(principal.FindFirst("sid")?.Value, out var id)
            ? id
            : null;
    }
}
