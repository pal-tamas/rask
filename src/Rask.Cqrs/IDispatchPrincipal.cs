using System.ComponentModel;
using System.Security.Claims;

namespace Rask.Cqrs;

/// <summary>
///     Who the work in flight is for, which is who a handler's <c>[Authorize]</c> is checked against on a local
///     dispatch. Registered by the host; Rask.Cqrs knows nothing about sessions or sign-in.
/// </summary>
/// <remarks>
///     Null means the work is nobody's — a background job, a durable handler, a hosted service, startup code —
///     and it runs as the system, unchecked. An anonymous visitor is a principal, and is checked.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IDispatchPrincipal
{
    /// <summary>The principal of the session or request in flight, or <see langword="null" /> outside one.</summary>
    ClaimsPrincipal? Current { get; }
}
