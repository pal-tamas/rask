using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Rask.Meta.Hosting;

/// <summary>
///     Registration for the meta framework front-end health check.
/// </summary>
public static class RaskMetaHealthCheckExtensions
{
    /// <summary>
    ///     Adds a check reporting whether the front end is listening and this instance is still
    ///     forwarding. Requires <c>AddRaskMeta()</c>. Usage:
    ///     <c>services.AddHealthChecks().AddRaskMetaFrontEnd();</c>
    ///     <para>
    ///         Tagged <c>ready</c>, which is the documented way to build a readiness probe:
    ///         <c>app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c =&gt; c.Tags.Contains("ready") })</c>.
    ///     </para>
    /// </summary>
    /// <param name="builder">The health-checks builder.</param>
    /// <param name="name">The check's registration name.</param>
    /// <param name="failureStatus">The status reported on failure. Defaults to the check's own.</param>
    /// <param name="tags">Optional tags, replacing the default <c>ready</c>.</param>
    /// <returns><paramref name="builder" />, for chaining.</returns>
    public static IHealthChecksBuilder AddRaskMetaFrontEnd(
        this IHealthChecksBuilder builder,
        string name = "rask_meta_front_end",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // An explicit factory rather than AddCheck<T>. The health-check infrastructure activates a
        // check through ActivatorUtilities, which considers only PUBLIC constructors, and this one
        // takes internal services — so AddCheck<T> would compile and then throw on the first probe.
        // Rask.Server hit exactly this and wrote it down; the note is what saved doing it twice.
        builder.Add(new HealthCheckRegistration(
            name,
            sp => new RaskMetaHealthCheck(
                sp.GetRequiredService<NodeReadiness>(),
                sp.GetRequiredService<MetaDrain>()),
            failureStatus,
            tags ?? ["ready"]));

        return builder;
    }
}
