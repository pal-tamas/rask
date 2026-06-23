using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net.WebSockets;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Diagnostics;

[Collection("SessionGracePeriod")]
public class WsLoopMetricsTests
{
    [Fact]
    public async Task HandlerDispatch_EmitsDispatchedCounter_AndDurationHistogram()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var initialHtml = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
        var sessionId = Markup.SessionId(initialHtml);
        var handlerId = Markup.FirstHandlerId(initialHtml);

        // Scope the listener to this host's metrics instance so parallel tests can't leak in.
        var metrics = host.Store.Metrics!;
        var counters = new ConcurrentDictionary<string, long>();
        var durationSamples = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (ReferenceEquals(inst.Meter, metrics.Meter))
                {
                    l.EnableMeasurementEvents(inst);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((inst, measurement, _, _) =>
            counters.AddOrUpdate(inst.Name, measurement, (_, v) => v + measurement));
        listener.SetMeasurementEventCallback<double>((inst, _, _, _) =>
        {
            if (inst.Name == "rask.handler.duration")
            {
                Interlocked.Increment(ref durationSamples);
            }
        });
        listener.Start();

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(5));

        // Awaiting the render reply guarantees the server-side dispatch (and its instrumentation)
        // has completed before we assert.
        await ws.SendJsonAsync(new { id = handlerId });
        var reply = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(reply);

        // The render frame is sent inside the dispatch's try; the duration histogram is recorded in
        // its finally (so it includes the send). Receiving the reply therefore does not guarantee the
        // finally has run yet — poll briefly rather than assume synchrony.
        var ok = await WaitUntil(
            () => counters.GetValueOrDefault("rask.handlers.dispatched") >= 1
                  && Volatile.Read(ref durationSamples) >= 1,
            TimeSpan.FromSeconds(2));

        Assert.True(ok,
            $"expected a dispatched counter and a duration sample; " +
            $"dispatched={counters.GetValueOrDefault("rask.handlers.dispatched")}, durationSamples={durationSamples}");
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }
}
