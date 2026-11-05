using System.Security.Claims;

namespace Rask.Core.Authentication;

public sealed record PendingAuth(
    AuthAction Action,
    ClaimsPrincipal? Principal,
    string? ReturnUrl,
    string? Scheme,
    bool Persistent = false)
{
    // The app's own token, for a sign-in into the session cookie. Internal, so the record's printed form never shows it.
    internal string? Token { get; init; }

    // The tenant an administrator chose to act in, for an app whose own sign-in keeps it (MapRaskTenantChoice).
    internal TenantPick? TenantPick { get; init; }

    internal static PendingAuth ChooseTenant(string? tenant, string? returnUrl) =>
        new(AuthAction.SignIn, null, returnUrl, null) { TenantPick = new TenantPick(tenant) };

    internal static PendingAuth SignInWithToken(string token, string? returnUrl) =>
        new(AuthAction.SignIn, null, returnUrl, null) { Token = token };

    internal static PendingAuth SignIn(ClaimsPrincipal principal, string? returnUrl, string? scheme, bool persistent) =>
        new(AuthAction.SignIn, principal, returnUrl, scheme, persistent);

    internal static PendingAuth SignOut(string? returnUrl, string? scheme) =>
        new(AuthAction.SignOut, null, returnUrl, scheme);
}
