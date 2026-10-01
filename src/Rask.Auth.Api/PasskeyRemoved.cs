using Rask.Cqrs;

namespace Rask.Auth;

/// <summary>A user removed a passkey. It can no longer sign anybody in.</summary>
/// <param name="UserId">The user's id.</param>
/// <param name="PasskeyId">The removed passkey's id.</param>
/// <param name="Name">What the user called it.</param>
[LocalOnly]
public sealed record PasskeyRemoved(Guid UserId, Guid PasskeyId, string Name) : IEvent;
