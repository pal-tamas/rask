using Rask.Cqrs;

namespace Rask.Auth;

/// <summary>A user set a new password from a reset link. Every session they had has ended.</summary>
/// <param name="UserId">The user's id.</param>
public sealed record PasswordReset(Guid UserId) : INotification;
