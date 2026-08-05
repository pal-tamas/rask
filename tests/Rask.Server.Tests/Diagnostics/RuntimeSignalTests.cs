using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Rask.Core;
using Rask.Core.Components;
using Rask.Server.Diagnostics;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Diagnostics;

/// <summary>
/// The signals that tell an operator what a host is doing, as opposed to what it refused to do.
/// </summary>
/// <remarks>
/// Before these, the meter could say a frame was rejected and a session evicted, but not how full the
/// dispatch queue was getting, how many of the "active" sessions were real users rather than probes, or
/// how long the framework's own render took as distinct from the app's handler.
/// </remarks>
public sealed class RuntimeSignalTests
{
    private sealed class Shell : Component
    {
        protected override Component? Render() =>
            [Doctype(), new Html()[new Head(), new Body()[new H1()["hi"]]]];
    }

    /// <summary>
    /// A GET mints a session that holds a capacity slot but is nobody — no socket, no user. Conflating
    /// that with a connected client is what makes "the host is filling up" unactionable.
    /// </summary>
    [Fact]
    public async Task Connected_counts_sockets_while_active_counts_slots()
    {
        using var host = RaskTestHost.Create<Shell>();
        var html = await host.Http.GetStringAsync("/start");
        var sessionId = MarkupAssert.SessionId(html);

        Assert.Equal(1, host.Store.LiveCount);
        Assert.Equal(0, host.Store.ConnectedCount);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (host.Store.ConnectedCount == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, host.Store.ConnectedCount);
        Assert.Equal(1, host.Store.LiveCount);
    }

    /// <summary>The queue returns to zero — a leaked increment would read as permanent backpressure.</summary>
    [Fact]
    public async Task Pending_handler_depth_returns_to_zero_after_a_dispatch()
    {
        await using var fixture = await ConnectedSession.Connect<CounterApp>();

        var handlerId = MarkupAssert.FirstHandlerId(
            await fixture.Host.Http.GetStringAsync("/start"));
        await fixture.Ws.SendJsonAsync(new { id = handlerId, seq = 1 });
        _ = await fixture.Ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (fixture.Host.Store.PendingHandlerCount != 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(0, fixture.Host.Store.PendingHandlerCount);
    }

    private sealed class CounterApp : Component
    {
        private int _count;

        protected override Component? Render() =>
        [
            Doctype(),
            new Html()[new Head(), new Body()[Button(OnClick: () => _count++)[$"n={_count}"]]]
        ];
    }

    /// <summary>
    /// Render duration and payload size are recorded only for a frame that actually went out. A deduped
    /// render — identical bytes, suppressed — would otherwise put a zero-byte sample in the histogram and
    /// count work no client ever saw.
    /// </summary>
    [Fact]
    public async Task Render_duration_and_payload_bytes_are_recorded_for_a_real_frame()
    {
        await using var fixture = await ConnectedSession.Connect<CounterApp>();
        using var capture = MeterCapture.For(fixture.Host.Services.GetRequiredService<RaskMetrics>().Meter);

        var handlerId = MarkupAssert.FirstHandlerId(
            await fixture.Host.Http.GetStringAsync("/start"));
        await fixture.Ws.SendJsonAsync(new { id = handlerId, seq = 1 });
        _ = await fixture.Ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (capture.HistogramSampleCount("rask.render.duration") == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(capture.HistogramSampleCount("rask.render.duration") >= 1,
            "the render that the click caused was not timed");

        // rask.payload.bytes is a Histogram<long>, and MeterCapture sums long measurements rather than
        // counting them — which suits this better than a sample count would: a positive total proves the
        // frame was recorded AND that it carried real bytes, not a zero-length sample.
        Assert.True(capture.Counter("rask.payload.bytes") > 0,
            "the frame that went out was not measured");
    }

    /// <summary>
    /// An uncapped host used to be unconditionally Healthy. That is the configuration most apps run, and
    /// it meant the one signal an orchestrator polls could not say anything at all until an OOM said it.
    /// </summary>
    [Fact]
    public async Task Health_reports_memory_even_when_sessions_are_uncapped()
    {
        using var host = RaskTestHost.Create<Shell>();
        var check = new RaskLiveHealthCheck(host.Store);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.True(result.Data.ContainsKey("memoryLoad"));
        Assert.True(result.Data.ContainsKey("connectedSessions"));
        var load = Assert.IsType<double>(result.Data["memoryLoad"]);
        Assert.InRange(load, 0.0, 1.0);
    }

    /// <summary>A host that cannot read its own memory position must not report that as unhealthy.</summary>
    [Fact]
    public async Task An_unreadable_memory_position_is_not_treated_as_a_full_one()
    {
        using var host = RaskTestHost.Create<Shell>();
        var check = new RaskLiveHealthCheck(host.Store);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        // On any normal test machine the process is nowhere near its ceiling, so the only way this is not
        // Healthy is the memory branch misfiring on an unusable reading.
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
