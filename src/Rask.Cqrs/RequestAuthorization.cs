using System.ComponentModel;

namespace Rask.Cqrs;

/// <summary>
///     What a handler's <c>[Authorize]</c> attributes ask of whoever sends its request. Emitted by the Rask.Cqrs
///     source generator for every handler that carries one and is not <c>[AllowAnonymous]</c>; you do not
///     construct one.
/// </summary>
/// <param name="roleSets">The roles of each <c>[Authorize(Roles = …)]</c>, one comma-separated set per attribute.</param>
/// <param name="policies">The policy of each <c>[Authorize(Policy = …)]</c>.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RequestAuthorization(IReadOnlyList<string> roleSets, IReadOnlyList<string> policies)
{
    /// <summary>One comma-separated set per attribute: the sender needs a role from every set.</summary>
    public IReadOnlyList<string> RoleSets { get; } = roleSets;

    /// <summary>Every policy the sender must meet.</summary>
    public IReadOnlyList<string> Policies { get; } = policies;
}
