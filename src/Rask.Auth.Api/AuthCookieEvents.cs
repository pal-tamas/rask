using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Rask.Auth;

/// <summary>
/// The cookie handler's side of sessions: a sign-in starts a row, a sign-out ends it, and every request resumes it.
/// </summary>
/// <remarks>
/// Every sign-in on every host ends in <c>HttpContext.SignInAsync</c> on the cookie scheme: the <c>/api/auth</c> endpoints
/// call it, and the Server host's ticket redeem calls it. So this is the one place that has to know about rows, and the
/// cookie itself carries only the session id beside the claims it is rebuilt from.
/// </remarks>
internal sealed class AuthCookieEvents(IAuthSessions sessions) : CookieAuthenticationEvents
{
    public override async Task SigningIn(CookieSigningInContext context)
    {
        // A principal that already names a session (a renewal), or that is not one of ours, is left as it is.
        if (context.Principal?.Identity is not ClaimsIdentity identity
            || AuthPrincipal.SessionId(context.Principal) is not null
            || AuthPrincipal.UserId(context.Principal) is not { } userId)
        {
            return;
        }

        var http = context.HttpContext;
        var userAgent = http.Request.Headers.UserAgent.ToString();
        var sessionId = await sessions
            .StartAsync(
                userId,
                http.Connection.RemoteIpAddress?.ToString(),
                userAgent.Length == 0 ? null : userAgent,
                context.Properties.IsPersistent,
                http.RequestAborted)
            .ConfigureAwait(false);

        identity.AddClaim(new Claim(AuthPrincipal.SessionClaim, sessionId.ToString()));
    }

    public override async Task SigningOut(CookieSigningOutContext context)
    {
        if (AuthPrincipal.SessionId(context.HttpContext.User) is { } sessionId)
        {
            await sessions.EndAsync(sessionId, context.HttpContext.RequestAborted).ConfigureAwait(false);
        }
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        // A cookie with no session id predates sessions, or was forged without the key: either way it is not honoured.
        var principal = AuthPrincipal.SessionId(context.Principal) is { } sessionId
            ? await sessions.ResumeAsync(sessionId, context.HttpContext.RequestAborted).ConfigureAwait(false)
            : null;

        if (principal is null)
        {
            context.RejectPrincipal();
            await context.HttpContext
                .SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)
                .ConfigureAwait(false);
            return;
        }

        context.ReplacePrincipal(principal);
    }
}
