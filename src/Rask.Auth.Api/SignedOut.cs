using Rask.Cqrs;

namespace Rask.Auth;

/// <summary>A session ended: signed out, or ended from another device or by a password reset.</summary>
/// <param name="UserId">The user's id.</param>
/// <param name="SessionId">The ended session's id.</param>
[LocalOnly]
public sealed record SignedOut(Guid UserId, Guid SessionId) : IEvent;
