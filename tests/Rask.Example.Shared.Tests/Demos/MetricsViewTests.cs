using Rask.Example.Shared;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Demos;

// MetricsGauge / MetricsChart are driven through RaskTest.Render so the real framework fires
// their lifecycle hooks. A FakeMetricsFeed stands in for the producer so the test pushes
// updates by hand (Publish) — fully deterministic, no background timer.
public sealed partial class MetricsViewTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Gauge_RendersInitialSnapshot()
    {
        var feed = new FakeMetricsFeed(Snapshot(tick: 0, cpu: 50, jobs: 4));
        var page = RaskTest.Render(() => MetricsGauge, Services(feed));

        var html = page.Html;

        Assert.Contains("tick 0", html);
        Assert.Contains("50.0%", html);
    }

    [Fact]
    public void Gauge_RepaintsWhenFeedPublishes()
    {
        var feed = new FakeMetricsFeed(Snapshot(tick: 0, cpu: 50, jobs: 4));
        var page = RaskTest.Render(() => MetricsGauge, Services(feed));

        feed.Publish(Snapshot(tick: 7, cpu: 73.5, jobs: 9));
        var html = page.Render();

        Assert.Contains("tick 7", html);
        Assert.Contains("73.5%", html);
        Assert.Contains(">9<", html); // active jobs
    }

    [Fact]
    public void Gauge_SubscribesOnMount_AndUnsubscribesOnUnmount()
    {
        var feed = new FakeMetricsFeed(Snapshot(tick: 0, cpu: 50, jobs: 4));
        var mounted = true;
        var page = RaskTest.Render(() => mounted ? MetricsGauge : null, Services(feed));
        Assert.Equal(1, feed.SubscriberCount);

        mounted = false;
        page.Render();
        Assert.Equal(0, feed.SubscriberCount);

        // A tick after unmount must not throw (no live handler) and produces no value change.
        feed.Publish(Snapshot(tick: 99, cpu: 12, jobs: 1));
        Assert.Equal(0, feed.SubscriberCount);
    }

    [Fact]
    public void Chart_SubscribesAndRendersSvgFromHistory()
    {
        var feed = new FakeMetricsFeed(Snapshot(tick: 0, cpu: 50, jobs: 4));
        var mounted = true;
        var page = RaskTest.Render(() => mounted ? MetricsChart : null, Services(feed));

        var html = page.Html;
        Assert.Equal(1, feed.SubscriberCount);
        Assert.Contains("<svg", html);
        // Percentage-formatted axis labels (ValueFormat "0.0'%'"), not the default money.
        Assert.Contains("50.0%", html);
        Assert.DoesNotContain("$", html);

        mounted = false;
        page.Render();
        Assert.Equal(0, feed.SubscriberCount);
    }

    private static IServiceProvider Services(FakeMetricsFeed feed) =>
        LiveHost.Services((typeof(IMetricsFeed), feed));

    private static MetricsSnapshot Snapshot(int tick, double cpu, int jobs)
    {
        var sample = new MetricsSample(tick, cpu, jobs, DateTimeOffset.UnixEpoch.AddSeconds(tick));
        return new MetricsSnapshot(sample, new[] { sample });
    }
}
