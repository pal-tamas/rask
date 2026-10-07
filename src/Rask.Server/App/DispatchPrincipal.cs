using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask;

/// <summary>
///     Who a locally dispatched request is checked against: the user of the session or request in flight.
/// </summary>
/// <remarks>
///     A singleton that answers from the work in flight rather than a scoped service, so a dispatcher resolved from
///     anywhere — a singleton's included — can ask, and a scope a handler opens for itself is still the user's.
/// </remarks>
internal sealed class DispatchPrincipal : IDispatchPrincipal
{
    /// <inheritdoc />
    public ClaimsPrincipal? Current => Ambient.Services?.GetService<ClaimsPrincipalSource>()?.InFlight;
}
