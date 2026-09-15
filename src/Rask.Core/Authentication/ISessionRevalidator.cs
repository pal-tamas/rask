using System.Security.Claims;

namespace Rask.Core.Authentication;

/// <summary>
/// Asks whether a live session's principal is still signed in, and with which claims.
/// </summary>
/// <remarks>
/// A live page holds its principal for as long as the socket stays open, which can be hours. A host calls this before a
/// handler dispatch, at most every 30 seconds, so a session ended elsewhere (signed out on another device, a password reset,
/// a removed role) stops a page that is still open instead of leaving it signed in until it reconnects. Rask.Auth provides
/// one; an app without accounts has none and nothing is checked.
/// </remarks>
public interface ISessionRevalidator
{
    /// <summary>The principal as it stands now, or <see langword="null" /> when its session has ended.</summary>
    /// <param name="principal">The principal the session holds.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    ValueTask<ClaimsPrincipal?> RevalidateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
