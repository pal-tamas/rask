using Rask.Cqrs;

namespace Rask.Auth;

/// <summary>A device signed in.</summary>
/// <param name="UserId">The user's id.</param>
/// <param name="SessionId">The new session's id.</param>
[LocalOnly]
public sealed record SignedIn(Guid UserId, Guid SessionId) : INotification;
