using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Rask.Server.Diagnostics;

/// <summary>
///     Registration helpers for the Rask live-session health check.
/// </summary>
public static class RaskHealthCheckExtensions
{
    /// <summary>
    ///     Adds the <see cref="RaskLiveHealthCheck" /> (live-session capacity) to the health-checks
    ///     pipeline. Requires <c>AddRask()</c> to have registered the <see cref="LiveSessionStore" />.
    ///     Usage: <c>services.AddHealthChecks().AddRaskLiveSessions();</c>
    /// </summary>
    /// <param name="builder">The health-checks builder.</param>
    /// <param name="name">The health-check registration name.</param>
    /// <param name="failureStatus">
    ///     The status reported when the check fails. Defaults to the check's own
    ///     <see cref="HealthStatus.Unhealthy" /> result.
    /// </param>
    /// <param name="tags">Optional tags for filtering this check (e.g. <c>"ready"</c>, <c>"rask"</c>).</param>
    public static IHealthChecksBuilder AddRaskLiveSessions(
        this IHealthChecksBuilder builder,
        string name = "rask_live_sessions",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null) =>
        builder.AddCheck<RaskLiveHealthCheck>(name, failureStatus, tags ?? []);
}
