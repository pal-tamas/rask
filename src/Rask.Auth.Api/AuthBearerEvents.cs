using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Auth;

/// <summary>A bearer token names its session too, so ending the session ends the token.</summary>
internal static class AuthBearerEvents
{
    internal static async Task OnTokenValidated(TokenValidatedContext context)
    {
        var sessions = context.HttpContext.RequestServices.GetRequiredService<IAuthSessions>();

        var principal = AuthPrincipal.SessionId(context.Principal) is { } sessionId
            ? await sessions.ResumeAsync(sessionId, context.HttpContext.RequestAborted).ConfigureAwait(false)
            : null;

        if (principal is null)
        {
            context.Fail("The session this token belongs to has ended.");
            return;
        }

        context.Principal = principal;
    }
}
