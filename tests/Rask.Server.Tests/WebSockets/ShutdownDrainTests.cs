using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Rask.Core.Live;
using Rask.Server.Diagnostics;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

/// <summary>
///     The graceful shutdown drain. What shipped before aborted every socket the instant
///     <c>ApplicationStopping</c> fired: no close frame, so the browser saw an abnormal 1006 closure,
///     read the replacement process's <c>session/unknown</c> reply as an idle timeout, and told the user
///     "Your session timed out" on what the docs call a zero-downtime deploy.
/// </summary>
public class ShutdownDrainTests
{
    [Fact]
    public void The_shutdown_frame_has_the_exact_shape_the_client_branches_on()
    {
        // Asserted as a whole literal for the same reason as the hot-reload frame: rask.js switches on
        // `data.type` as a literal string, so a rename on either side would leave both halves compiling
        // and silently restore the old, wrong behaviour.
        Assert.Equal("""{"type":"shutdown","status":"draining"}""", LivePayload.ServerShutdownJson);
        Assert.Equal(
            LivePayload.ServerShutdownJson,
            Encoding.UTF8.GetString(LivePayload.ServerShutdownFrame));
    }

    [Fact]
    public async Task A_connected_client_is_told_the_server_is_going_away()
    {
        using var host = RaskTestHost.Create<TestApp>();
        using var ws = await ConnectAsync(host);

        await host.StopAsync();

        Assert.Equal(LivePayload.ServerShutdownJson, await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task The_socket_is_closed_with_going_away_not_aborted()
    {
        using var host = RaskTestHost.Create<TestApp>();
        using var ws = await ConnectAsync(host);

        await host.StopAsync();

        // The announcement rides the same render lock as the close, and TCP preserves order, so the
        // frame is always ahead of the close on the wire.
        Assert.Equal(LivePayload.ServerShutdownJson, await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2)));

        var close = await ws.TryReceiveCloseAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(close);
        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, close.Value.Status);
        Assert.Equal("server-shutdown", close.Value.Reason);
    }

    [Fact]
    public async Task An_in_flight_handler_finishes_instead_of_being_cancelled_mid_call()
    {
        // The regression that matters most behind the UI: a click that is mid-SaveChangesAsync used to
        // be cancelled and dropped, because the socket's token was ApplicationStopping itself.
        DrainGateApp.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = RaskTestHost.Create<DrainGateApp>();
        var html = await host.Http.GetStringAsync("/start");
        var sessionId = MarkupAssert.SessionId(html);
        var handlerId = MarkupAssert.FirstHandlerId(html);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        var session = host.Store.Get(sessionId)!;
        await ws.SendJsonAsync(new { id = handlerId });
        await WaitForAsync(() => session.PendingHandlers > 0, TimeSpan.FromSeconds(2));

        var stop = host.StopAsync();

        // The drain is settling on this handler, so the stop must not have completed yet.
        await Task.Delay(150);
        Assert.False(stop.IsCompleted);

        DrainGateApp.Gate.SetResult();
        await stop;

        Assert.Equal(0, session.PendingHandlers);
    }

    [Fact]
    public async Task Sessions_are_disposed_by_the_time_the_stop_returns()
    {
        // Not "eventually, via container teardown" — the old path fired an unawaited RemoveAsync, so a
        // component's async unmount raced process exit with nobody observing it.
        using var host = RaskTestHost.Create<TestApp>();
        using var ws = await ConnectAsync(host);
        Assert.Equal(1, host.Store.Count);

        await host.StopAsync();

        Assert.Equal(0, host.Store.Count);
    }

    [Fact]
    public async Task A_handler_that_never_returns_costs_the_budget_not_the_shutdown()
    {
        // Own app again, not HangingApp: HandlerBackpressureTests owns that static gate, and xUnit runs the
        // two classes in parallel.
        DrainGateApp.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var host = RaskTestHost.Create<DrainGateApp>(
                configureServer: o => o.ShutdownDrainTimeout = TimeSpan.FromMilliseconds(200));
            var html = await host.Http.GetStringAsync("/start");
            var sessionId = MarkupAssert.SessionId(html);
            var handlerId = MarkupAssert.FirstHandlerId(html);

            using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
            await ws.SendJsonAsync(new { id = handlerId });

            var session = host.Store.Get(sessionId)!;
            await WaitForAsync(() => session.PendingHandlers > 0, TimeSpan.FromSeconds(2));

            var started = Environment.TickCount64;
            await host.StopAsync();

            // The backstop is what bounds this: the budget elapses, the socket is aborted, teardown runs.
            Assert.True(Environment.TickCount64 - started < 5_000,
                "a wedged handler must not hold the host open past the drain budget");
            Assert.Equal(0, host.Store.Count);
        }
        finally
        {
            DrainGateApp.Gate.TrySetResult();
        }
    }

    [Fact]
    public async Task A_draining_host_refuses_new_sessions_and_says_to_retry_at_once()
    {
        using var host = RaskTestHost.Create<TestApp>();
        await host.StopAsync();

        var response = await host.Http.GetAsync("/start");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        // Distinct from the capacity 503's Retry-After: 5 — the replacement instance is already up.
        Assert.Equal("1", response.Headers.GetValues("Retry-After").Single());
        Assert.Equal(0, host.Store.Count);
    }

    [Fact]
    public async Task Readiness_goes_unhealthy_while_draining_but_capacity_still_reports_capacity()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var drain = host.Services.GetRequiredService<RaskDrainCoordinator>();
        var readiness = new RaskReadinessHealthCheck(drain);
        var context = new HealthCheckContext();

        Assert.Equal(HealthStatus.Healthy, (await readiness.CheckHealthAsync(context)).Status);

        await host.StopAsync();

        Assert.Equal(HealthStatus.Unhealthy, (await readiness.CheckHealthAsync(context)).Status);
        // The capacity check keeps its own meaning — an empty store is not "at capacity".
        Assert.Equal(
            HealthStatus.Healthy,
            (await new RaskLiveHealthCheck(host.Store).CheckHealthAsync(context)).Status);
    }

    [Fact]
    public async Task The_health_endpoint_answers_503_while_draining()
    {
        // Probed through the real pipeline, not by constructing the check: the health-check
        // infrastructure activates checks through ActivatorUtilities, which only sees public
        // constructors. A direct-construction test passes happily while every actual probe throws.
        using var host = RaskTestHost.Create<TestApp>(
            configureServices: s => s.AddHealthChecks().AddRaskLiveSessions(),
            configureMiddleware: app => app.UseHealthChecks("/health"));

        var before = await host.Http.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal("Healthy", await before.Content.ReadAsStringAsync());

        await host.StopAsync();

        var during = await host.Http.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, during.StatusCode);
    }

    [Fact]
    public async Task The_drain_still_works_when_hosted_services_stop_concurrently()
    {
        // The scaffold sets HostOptions.ServicesStopConcurrently = true so the batteries' shutdown grace
        // periods overlap this drain instead of summing ahead of it. That removes the reverse-registration
        // stop ORDER the drain would otherwise lean on — so this pins that it doesn't need it. It works
        // because Kestrel's own StopAsync waits for in-flight requests, and a WebSocket is an in-flight
        // request: the drain runs while Kestrel waits.
        using var host = RaskTestHost.Create<TestApp>(
            configureServices: s => s.Configure<HostOptions>(o => o.ServicesStopConcurrently = true));
        using var ws = await ConnectAsync(host);

        await host.StopAsync();

        Assert.Equal(LivePayload.ServerShutdownJson, await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2)));
        var close = await ws.TryReceiveCloseAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(close);
        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, close.Value.Status);
        Assert.Equal(0, host.Store.Count);
    }

    [Fact]
    public async Task A_zero_budget_disables_the_drain_and_restores_the_old_abort()
    {
        // The documented opt-out. Worth pinning: it is the escape hatch for anyone whose shutdown the
        // drain would otherwise lengthen, and it is easy to break without noticing.
        using var host = RaskTestHost.Create<TestApp>(
            configureServer: o => o.ShutdownDrainTimeout = TimeSpan.Zero);
        using var ws = await ConnectAsync(host);

        await host.StopAsync();

        Assert.Null(await ws.TryReceiveCloseAsync(TimeSpan.FromSeconds(2)));
    }

    private static async Task<WebSocket> ConnectAsync(RaskTestHost host)
    {
        var sessionId = MarkupAssert.SessionId(await host.Http.GetStringAsync("/start"));
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        return ws;
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "condition was not met within the timeout");
    }
}
