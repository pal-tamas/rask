using System.ComponentModel;

namespace Rask.Cqrs;

/// <summary>
///     What a handler's <c>[Authorize]</c> asks of whoever sends its request. Emitted by the Rask.Cqrs source
///     generator for every handler that carries one and is not <c>[AllowAnonymous]</c>; you do not construct one.
/// </summary>
/// <param name="roles">The roles named by <c>[Authorize(Roles = …)]</c>, comma-separated, or null.</param>
/// <param name="policy">The policy named by <c>[Authorize(Policy = …)]</c>, or null.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RequestAuthorization(string? roles, string? policy)
{
    /// <summary>The roles the sender must hold one of, comma-separated, or null for any signed-in user.</summary>
    public string? Roles { get; } = roles;

    /// <summary>The policy the sender must meet, or null.</summary>
    public string? Policy { get; } = policy;
}
