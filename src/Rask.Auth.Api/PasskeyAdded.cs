using Rask.Cqrs;

namespace Rask.Auth;

/// <summary>A user added a passkey.</summary>
/// <param name="UserId">The user's id.</param>
/// <param name="PasskeyId">The new passkey's id.</param>
/// <param name="Name">What the user called it.</param>
[LocalOnly]
public sealed record PasskeyAdded(Guid UserId, Guid PasskeyId, string Name) : IEvent;
