using Rask.Core.Authentication;

namespace Rask.Auth;

/// <summary>Checks a live session's principal against its <see cref="Session" /> row.</summary>
internal sealed class SessionRevalidator(IAuthSessions sessions) : ISessionRevalidator
{
    public async ValueTask<System.Security.Claims.ClaimsPrincipal?> Revalidate(
        System.Security.Claims.ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return AuthPrincipal.SessionId(principal) is { } sessionId
            ? await sessions.ResumeAsync(sessionId, cancellationToken).ConfigureAwait(false)
            : null;
    }
}
