using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Rask.Meta.Hosting;

/// <summary>
///     Reports whether the meta framework's own server is up and this instance is still willing to
///     forward to it.
/// </summary>
/// <remarks>
///     <para>
///         This is what makes a readiness probe mean something in this lane. Kestrel answers as soon as
///         it binds, seconds before the front end finishes booting — so without this an orchestrator
///         routes traffic at an instance that can only return 503, and reports the deploy as healthy
///         while every page is unavailable.
///     </para>
///     <para>
///         Unhealthy for two distinct reasons, and it says which. <em>Starting</em> is expected and
///         resolves itself; <em>draining</em> means this instance is on its way out and a load balancer
///         should stop routing at it. Both are the same HTTP answer to a probe and very different
///         answers to an operator reading the log at 3am.
///     </para>
/// </remarks>
public sealed class RaskMetaHealthCheck : IHealthCheck
{
    // Cached: this runs on every probe and none of the results carry per-call data.
    private static readonly HealthCheckResult _draining =
        HealthCheckResult.Unhealthy("The front end is draining; this instance is shutting down.");

    private static readonly HealthCheckResult _starting =
        HealthCheckResult.Unhealthy("The front end is not listening yet.");

    private static readonly HealthCheckResult _ready =
        HealthCheckResult.Healthy("The front end is listening.");

    private readonly MetaDrain _drain;
    private readonly NodeReadiness _readiness;

    internal RaskMetaHealthCheck(NodeReadiness readiness, MetaDrain drain)
    {
        _readiness = readiness;
        _drain = drain;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_drain.IsDraining ? _draining : _readiness.IsReady ? _ready : _starting);
}
