using System.Security.Claims;

namespace Rask.Core.Authentication;

public sealed record PendingAuth(
    AuthAction Action,
    ClaimsPrincipal? Principal,
    string? ReturnUrl,
    string? Scheme,
    bool Persistent = false)
{
    internal static PendingAuth SignIn(ClaimsPrincipal principal, string? returnUrl, string? scheme, bool persistent) =>
        new(AuthAction.SignIn, principal, returnUrl, scheme, persistent);

    internal static PendingAuth SignOut(string? returnUrl, string? scheme) =>
        new(AuthAction.SignOut, null, returnUrl, scheme);
}
