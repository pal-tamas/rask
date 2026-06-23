using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using Rask.Server.Diagnostics;

namespace Rask.Server.Tests.Diagnostics;

public class RaskMetricsTests
{
    [Fact]
    public void SessionLifecycle_EmitsCreatedRejectedEvictedCounters()
    {
        using var metrics = new RaskMetrics();
        var counts = ListenCounters(metrics);

        var store = NewStore(metrics);
        store.MaxSessions = 1;

        var s1 = store.TryCreate(_ => new BasicComponent());
        Assert.NotNull(s1);
        var s2 = store.TryCreate(_ => new BasicComponent()); // over cap → rejected
        Assert.Null(s2);
        store.Remove(s1!.Id); // evicted

        Assert.Equal(1, counts.GetValueOrDefault("rask.sessions.created"));
        Assert.Equal(1, counts.GetValueOrDefault("rask.sessions.rejected"));
        Assert.Equal(1, counts.GetValueOrDefault("rask.sessions.evicted"));
    }

    [Fact]
    public void ActiveSessions_ObservableGauge_ReportsLiveCount()
    {
        using var metrics = new RaskMetrics();
        var gauge = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (ReferenceEquals(inst.Meter, metrics.Meter) && inst.Name == "rask.sessions.active")
                {
                    l.EnableMeasurementEvents(inst);
                }
            }
        };
        listener.SetMeasurementEventCallback<int>((_, measurement, _, _) => gauge = measurement);
        listener.Start();

        var store = NewStore(metrics);
        store.Create(_ => new BasicComponent());
        store.Create(_ => new BasicComponent());

        listener.RecordObservableInstruments();
        Assert.Equal(2, gauge);
    }

    [Fact]
    public void FrameRejected_TagsTheReason()
    {
        using var metrics = new RaskMetrics();
        var reasons = new ConcurrentBag<string?>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (ReferenceEquals(inst.Meter, metrics.Meter) && inst.Name == "rask.ws.frames.rejected")
                {
                    l.EnableMeasurementEvents(inst);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "reason")
                {
                    reasons.Add(tag.Value as string);
                }
            }
        });
        listener.Start();

        metrics.FrameRejected("rate");

        Assert.Contains("rate", reasons);
    }

    private static ConcurrentDictionary<string, long> ListenCounters(RaskMetrics metrics)
    {
        var counts = new ConcurrentDictionary<string, long>();
        var listener = new MeterListener
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
            counts.AddOrUpdate(inst.Name, measurement, (_, v) => v + measurement));
        listener.Start();
        return counts;
    }

    private static LiveSessionStore NewStore(RaskMetrics metrics)
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        return new LiveSessionStore(sp.GetRequiredService<IServiceScopeFactory>(), null, metrics);
    }

    private sealed class BasicComponent : Component
    {
        protected override RenderResult Render() => new Span();
    }
}
