using Rask.Cqrs;

namespace Rask.Auth;

/// <summary>Somebody registered. Published after the user row commits, through the outbox when there is one.</summary>
/// <param name="UserId">The new user's id.</param>
/// <param name="Email">The address they registered with.</param>
public sealed record UserRegistered(Guid UserId, string Email) : INotification;

/// <summary>A user confirmed their address.</summary>
/// <param name="UserId">The user's id.</param>
/// <param name="Email">The confirmed address.</param>
public sealed record EmailConfirmed(Guid UserId, string Email) : INotification;

/// <summary>A user set a new password from a reset link. Every session they had has ended.</summary>
/// <param name="UserId">The user's id.</param>
public sealed record PasswordReset(Guid UserId) : INotification;

/// <summary>A device signed in.</summary>
/// <param name="UserId">The user's id.</param>
/// <param name="SessionId">The new session's id.</param>
public sealed record SignedIn(Guid UserId, Guid SessionId) : INotification;

/// <summary>A session ended: signed out, or ended from another device or by a password reset.</summary>
/// <param name="UserId">The user's id.</param>
/// <param name="SessionId">The ended session's id.</param>
public sealed record SignedOut(Guid UserId, Guid SessionId) : INotification;

/// <summary>A user added a passkey.</summary>
/// <param name="UserId">The user's id.</param>
/// <param name="PasskeyId">The new passkey's id.</param>
/// <param name="Name">What the user called it.</param>
public sealed record PasskeyAdded(Guid UserId, Guid PasskeyId, string Name) : INotification;

/// <summary>A user removed a passkey. It can no longer sign anybody in.</summary>
/// <param name="UserId">The user's id.</param>
/// <param name="PasskeyId">The removed passkey's id.</param>
/// <param name="Name">What the user called it.</param>
public sealed record PasskeyRemoved(Guid UserId, Guid PasskeyId, string Name) : INotification;
