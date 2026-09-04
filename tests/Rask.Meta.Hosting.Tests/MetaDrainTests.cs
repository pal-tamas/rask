using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Rask.Meta.Hosting.Tests;

/// <summary>
///     The drain's own accounting, and the health check that reads it.
/// </summary>
public class MetaDrainTests
{
    /// <summary>
    ///     A drain that begins with nothing in flight is already finished.
    /// </summary>
    /// <remarks>
    ///     Every deploy of a quiet app takes this path. Without it the shutdown would sit out its whole
    ///     budget waiting for a counter that is already zero — turning a one-second stop into a
    ///     ten-second one, on the most common case there is.
    /// </remarks>
    [Fact]
    public async Task An_idle_drain_completes_at_once()
    {
        var drain = new MetaDrain();
        drain.BeginDrain();

        Assert.True(drain.IsDraining);
        Assert.True(await drain.WaitForIdleAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    /// <summary>The wait ends when the last in-flight request finishes.</summary>
    [Fact]
    public async Task The_wait_ends_when_the_last_request_finishes()
    {
        var drain = new MetaDrain();
        drain.Enter();
        drain.Enter();
        drain.BeginDrain();

        var waiting = drain.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        drain.Exit();
        Assert.False(waiting.IsCompleted, "one request is still in flight");

        drain.Exit();

        Assert.True(await waiting);
        Assert.Equal(0, drain.InFlight);
    }

    /// <summary>
    ///     A request that never finishes does not hold shutdown open for ever.
    /// </summary>
    /// <remarks>
    ///     Reported rather than thrown, so the caller can log how many were abandoned and stop anyway —
    ///     a deploy that hangs because one streamed response never ended is worse than a dropped
    ///     response.
    /// </remarks>
    [Fact]
    public async Task A_stuck_request_does_not_block_shutdown_for_ever()
    {
        var drain = new MetaDrain();
        drain.Enter();
        drain.BeginDrain();

        Assert.False(await drain.WaitForIdleAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
        Assert.Equal(1, drain.InFlight);
    }

    /// <summary>
    ///     The health check distinguishes starting from draining.
    /// </summary>
    /// <remarks>
    ///     Both are 503 to a probe and very different things to an operator: starting resolves itself,
    ///     draining means this instance is leaving and a load balancer should stop routing at it.
    /// </remarks>
    [Fact]
    public async Task The_health_check_reports_starting_ready_and_draining_apart()
    {
        var readiness = new NodeReadiness();
        var drain = new MetaDrain();
        var check = new RaskMetaHealthCheck(readiness, drain);
        var context = new HealthCheckContext();

        var starting = await check.CheckHealthAsync(context);
        Assert.Equal(HealthStatus.Unhealthy, starting.Status);
        Assert.Contains("not listening", starting.Description, StringComparison.Ordinal);

        readiness.MarkReady();
        var ready = await check.CheckHealthAsync(context);
        Assert.Equal(HealthStatus.Healthy, ready.Status);

        drain.BeginDrain();
        var draining = await check.CheckHealthAsync(context);
        Assert.Equal(HealthStatus.Unhealthy, draining.Status);
        Assert.Contains("draining", draining.Description, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Draining outranks readiness.
    /// </summary>
    /// <remarks>
    ///     An instance on its way out is not ready even while its front end is still perfectly healthy —
    ///     that is the whole point of a readiness probe during a rolling deploy.
    /// </remarks>
    [Fact]
    public async Task Draining_wins_over_a_listening_front_end()
    {
        var readiness = new NodeReadiness();
        readiness.MarkReady();
        var drain = new MetaDrain();
        drain.BeginDrain();

        var result = await new RaskMetaHealthCheck(readiness, drain).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
