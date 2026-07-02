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
        using var capture = MeterCapture.For(metrics.Meter);

        var store = NewStore(metrics);
        store.MaxSessions = 1;

        var s1 = store.TryCreate(_ => new BasicComponent());
        Assert.NotNull(s1);
        var s2 = store.TryCreate(_ => new BasicComponent()); // over cap → rejected
        Assert.Null(s2);
        store.Remove(s1!.Id); // evicted

        Assert.Equal(1, capture.Counter("rask.sessions.created"));
        Assert.Equal(1, capture.Counter("rask.sessions.rejected"));
        Assert.Equal(1, capture.Counter("rask.sessions.evicted"));
    }

    [Fact]
    public async Task DisposeAsync_EvictsRemainingSessions_EmitsEvictedCounter()
    {
        using var metrics = new RaskMetrics();
        using var capture = MeterCapture.For(metrics.Meter);

        var store = NewStore(metrics);
        store.Create(_ => new BasicComponent());
        store.Create(_ => new BasicComponent());

        await store.DisposeAsync(); // shutdown teardown must also count as eviction

        Assert.Equal(2, capture.Counter("rask.sessions.created"));
        Assert.Equal(2, capture.Counter("rask.sessions.evicted"));
    }

    [Fact]
    public void ActiveSessions_ObservableGauge_ReportsLiveCount()
    {
        using var metrics = new RaskMetrics();
        using var capture = MeterCapture.For(metrics.Meter);

        var store = NewStore(metrics);
        store.Create(_ => new BasicComponent());
        store.Create(_ => new BasicComponent());

        capture.RecordObservable();
        Assert.Equal(2, capture.Gauge("rask.sessions.active"));
    }

    [Fact]
    public void FrameRejected_TagsTheReason()
    {
        using var metrics = new RaskMetrics();
        using var capture = MeterCapture.For(metrics.Meter);

        metrics.FrameRejected("rate");

        Assert.Contains("rate", capture.TagValues("rask.ws.frames.rejected", "reason"));
    }

    private static LiveSessionStore NewStore(RaskMetrics metrics)
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        return new LiveSessionStore(sp.GetRequiredService<IServiceScopeFactory>(), null, metrics);
    }

    private sealed class BasicComponent : Component
    {
        protected override Component? Render() => new Span();
    }
}
