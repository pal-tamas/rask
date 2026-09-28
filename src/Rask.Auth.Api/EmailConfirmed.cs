using Rask.Cqrs;

namespace Rask.Auth;

/// <summary>A user confirmed their address.</summary>
/// <param name="UserId">The user's id.</param>
/// <param name="Email">The confirmed address.</param>
[LocalOnly]
public sealed record EmailConfirmed(Guid UserId, string Email) : INotification;
