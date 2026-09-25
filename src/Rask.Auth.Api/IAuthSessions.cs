using System.Security.Claims;

namespace Rask.Auth;

/// <summary>Starts, resumes and ends <see cref="Session" /> rows, without naming the user or context type.</summary>
internal interface IAuthSessions
{
    /// <summary>Starts a session for <paramref name="userId" />, returning its id.</summary>
    Task<Guid> StartAsync(
        Guid userId, string? ipAddress, string? userAgent, bool persistent, CancellationToken cancellationToken = default);

    /// <summary>
    /// The principal for a live session, freshly built from its user, or <see langword="null" /> when the session has
    /// ended, expired, or its user is gone.
    /// </summary>
    Task<ClaimsPrincipal?> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Ends one session.</summary>
    Task EndAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Ends every session of <paramref name="userId" />, except <paramref name="except" />.</summary>
    Task<int> EndAllAsync(Guid userId, Guid? except = null, CancellationToken cancellationToken = default);
}
