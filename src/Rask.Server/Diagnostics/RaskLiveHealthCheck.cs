using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Rask.Server.Diagnostics;

/// <summary>
///     An ASP.NET health check reporting live-session capacity. <c>Healthy</c> while comfortably below
///     the configured <see cref="LiveSessionStore.MaxSessions" /> cap, <c>Degraded</c> at or above 80%
///     of it (the host is filling up), and <c>Unhealthy</c> once at the cap (new sessions are being
///     refused with 503). When sessions are uncapped (<c>MaxSessions == 0</c>) it is always
///     <c>Healthy</c> and simply reports the active count. The active/max counts are attached as
///     health-check data for dashboards.
/// </summary>
public sealed class RaskLiveHealthCheck(LiveSessionStore store) : IHealthCheck
{
    internal const double DegradedRatio = 0.8;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // LiveCount (reservations + committed), not Count (committed only): admission refuses new
        // sessions at LiveCount == MaxSessions, so the health status must track the same number or it
        // would report Healthy while the host is already answering 503s during a concurrent GET burst.
        var active = store.LiveCount;
        var max = store.MaxSessions;
        var data = new Dictionary<string, object>
        {
            ["activeSessions"] = active,
            ["maxSessions"] = max
        };

        if (max <= 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                $"Rask live sessions: {active} active (uncapped).", data));
        }

        if (active >= max)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Rask live sessions at capacity ({active}/{max}); new sessions are being refused.", null, data));
        }

        if (active >= max * DegradedRatio)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Rask live sessions near capacity ({active}/{max}).", null, data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Rask live sessions: {active}/{max}.", data));
    }
}
