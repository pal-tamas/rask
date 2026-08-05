using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Rask.Server.Diagnostics;

/// <summary>
///     Registration helpers for the Rask live-session health check.
/// </summary>
public static class RaskHealthCheckExtensions
{
    /// <summary>
    ///     Adds the Rask health checks to the pipeline. Requires <c>AddRask()</c> to have registered the
    ///     <see cref="LiveSessionStore" />. Usage:
    ///     <c>services.AddHealthChecks().AddRaskLiveSessions();</c>
    ///     <para>
    ///         Registers two checks, because they answer different questions:
    ///         <see cref="RaskLiveHealthCheck" /> (tagged <c>live</c>) reports live-session
    ///         <em>capacity</em>, and <see cref="RaskReadinessHealthCheck" /> (tagged <c>ready</c>)
    ///         reports whether this instance is still accepting sessions at all — it goes unhealthy the
    ///         moment a graceful shutdown begins. An aggregate endpoint therefore returns 503 while
    ///         draining, with no extra wiring.
    ///     </para>
    ///     <para>
    ///         Split them with the tags when you want separate probes:
    ///         <c>app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c =&gt; c.Tags.Contains("ready") })</c>.
    ///     </para>
    /// </summary>
    /// <param name="builder">The health-checks builder.</param>
    /// <param name="name">The capacity check's registration name. The readiness check appends <c>_ready</c>.</param>
    /// <param name="failureStatus">
    ///     The status reported when the capacity check fails. Defaults to the check's own
    ///     <see cref="HealthStatus.Unhealthy" /> result.
    /// </param>
    /// <param name="tags">
    ///     Optional tags for the capacity check, replacing the default <c>live</c>. The readiness check
    ///     always carries <c>ready</c> — that tag is the documented way to build a readiness probe.
    /// </param>
    public static IHealthChecksBuilder AddRaskLiveSessions(
        this IHealthChecksBuilder builder,
        string name = "rask_live_sessions",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        builder.AddCheck<RaskLiveHealthCheck>(name, failureStatus, tags ?? ["live"]);
        // Registered with an explicit factory rather than AddCheck<T>: the health-check infrastructure
        // activates a check through ActivatorUtilities, which only considers PUBLIC constructors, and
        // this one takes the internal drain coordinator. AddCheck<T> would compile and then throw on the
        // first probe — which is how it was caught.
        builder.Add(new HealthCheckRegistration(
            name + "_ready",
            sp => new RaskReadinessHealthCheck(sp.GetRequiredService<RaskDrainCoordinator>()),
            failureStatus: null,
            tags: ["ready"]));
        return builder;
    }
}
