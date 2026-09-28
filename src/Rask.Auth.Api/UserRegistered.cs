using Rask.Cqrs;

namespace Rask.Auth;

/// <summary>Somebody registered. Published after the user row commits, through the outbox when there is one.</summary>
/// <param name="UserId">The new user's id.</param>
/// <param name="Email">The address they registered with.</param>
[LocalOnly]
public sealed record UserRegistered(Guid UserId, string Email) : INotification;
