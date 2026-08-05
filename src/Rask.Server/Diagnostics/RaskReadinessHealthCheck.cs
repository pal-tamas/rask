using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Rask.Server.Diagnostics;

/// <summary>
///     An ASP.NET health check reporting whether this instance is still accepting live sessions.
///     <c>Unhealthy</c> from the moment a graceful shutdown begins, so a proxy or load balancer with
///     active probes stops routing at a host that is on its way out — and an operator watching
///     <c>/health</c> can see the difference between "draining" and "dead".
///     <para>
///         Deliberately separate from <see cref="RaskLiveHealthCheck" /> rather than folded into it.
///         That check's <c>Unhealthy</c> already means one specific thing — "at the session cap,
///         refusing with 503" — and overloading it with a second cause would make both readings
///         ambiguous exactly when someone is trying to diagnose a live incident.
///     </para>
/// </summary>
public sealed class RaskReadinessHealthCheck : IHealthCheck
{
    // Cached: the check runs on every probe and neither result carries per-call data.
    private static readonly HealthCheckResult Draining =
        HealthCheckResult.Unhealthy("Rask is draining; this instance is no longer accepting live sessions.");

    private static readonly HealthCheckResult Ready =
        HealthCheckResult.Healthy("Rask is accepting live sessions.");

    private readonly RaskDrainCoordinator _drain;

    internal RaskReadinessHealthCheck(RaskDrainCoordinator drain) => _drain = drain;

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_drain.IsDraining ? Draining : Ready);
}
