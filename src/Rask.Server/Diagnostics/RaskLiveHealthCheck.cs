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

    /// <summary>
    ///     Memory load at which the host reports <c>Degraded</c> regardless of the session count, as a
    ///     fraction of what the runtime believes is available to it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A session cap alone cannot keep a host healthy, because a session's cost is a property of
    ///         the page rather than of the user: the same host holds ~66,000 sessions of a trivial page or
    ///         ~735 of a 200-row grid, a ~90× spread across two pages of the same app. A cap sized for the
    ///         small page is no protection on the big one, and a cap sized for the big one wastes the host
    ///         the rest of the time. So the memory itself is also watched, and it is the signal that
    ///         actually degrades before an orchestrator has to notice an OOM.
    ///     </para>
    ///     <para>
    ///         Read from <see cref="GCMemoryInfo" />, which honours a container memory limit — so this
    ///         reflects the cgroup ceiling a deployed app is really running under, not the size of the
    ///         machine underneath it.
    ///     </para>
    /// </remarks>
    internal const double DegradedMemoryLoad = 0.80;

    /// <summary>Memory load at which the host reports <c>Unhealthy</c>. Above this an OOM is close.</summary>
    internal const double UnhealthyMemoryLoad = 0.92;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // LiveCount (reservations + committed), not Count (committed only): admission refuses new
        // sessions at LiveCount == MaxSessions, so the health status must track the same number or it
        // would report Healthy while the host is already answering 503s during a concurrent GET burst.
        var active = store.LiveCount;
        var max = store.MaxSessions;
        var connected = store.ConnectedCount;
        var memoryLoad = MemoryLoad();
        var data = new Dictionary<string, object>
        {
            ["activeSessions"] = active,
            ["connectedSessions"] = connected,
            ["maxSessions"] = max,
            ["memoryLoad"] = Math.Round(memoryLoad, 3)
        };

        // Memory first: it outranks the session count in both directions. A host over its memory ceiling
        // is in trouble whatever the cap says, and a host well under the cap can still be — because what
        // a session costs depends on the page, not on the number of them.
        if (memoryLoad >= UnhealthyMemoryLoad)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Rask host memory at {memoryLoad:P0} of its limit with {active} sessions; shed load.",
                null, data));
        }

        if (max <= 0)
        {
            return memoryLoad >= DegradedMemoryLoad
                ? Task.FromResult(HealthCheckResult.Degraded(
                    $"Rask host memory at {memoryLoad:P0} of its limit with {active} sessions (uncapped).",
                    null, data))
                : Task.FromResult(HealthCheckResult.Healthy(
                    $"Rask live sessions: {active} active, {connected} connected (uncapped).", data));
        }

        if (active >= max)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Rask live sessions at capacity ({active}/{max}); new sessions are being refused.", null, data));
        }

        if (active >= max * DegradedRatio || memoryLoad >= DegradedMemoryLoad)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Rask host near capacity ({active}/{max} sessions, memory {memoryLoad:P0}).", null, data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Rask live sessions: {active}/{max} ({connected} connected).", data));
    }

    /// <summary>
    ///     Managed memory in use as a fraction of what the runtime believes it may use, or <c>0</c> when
    ///     the runtime won't say.
    /// </summary>
    /// <remarks>
    ///     <c>TotalAvailableMemoryBytes</c> is the container limit when there is one, so this is the
    ///     fraction that matters to the thing that will kill the process. Returning 0 on an unusable
    ///     reading is deliberate: an unknown memory position must not be reported as an unhealthy one, or
    ///     a host would shed load because it could not measure itself.
    /// </remarks>
    private static double MemoryLoad()
    {
        var info = GC.GetGCMemoryInfo();
        return info.TotalAvailableMemoryBytes <= 0
            ? 0
            : (double)info.MemoryLoadBytes / info.TotalAvailableMemoryBytes;
    }
}
