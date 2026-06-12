using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Demos;

// The background producer behind the /background showcase. The pure Step transition is
// tested deterministically (invariants only — Tick increments, history stays capped,
// Current is the last point); the live loop is tested through the real MetricsFeed with
// a fast interval, the same WaitFor approach LiveTickerTests uses.
public sealed class MetricsFeedTests
{
    [Fact]
    public void Step_IncrementsTick_AndAppendsCurrentAsLastPoint()
    {
        var initial = MetricsFeed.CreateInitialSnapshot(DateTimeOffset.UnixEpoch);

        var next = MetricsFeed.Step(initial, DateTimeOffset.UnixEpoch.AddSeconds(1));

        Assert.Equal(initial.Current.Tick + 1, next.Current.Tick);
        Assert.Equal(next.Current, next.Recent[^1]);
        Assert.Equal(2, next.Recent.Count);
    }

    [Fact]
    public void Step_DoesNotMutatePreviousSnapshot()
    {
        var initial = MetricsFeed.CreateInitialSnapshot(DateTimeOffset.UnixEpoch);
        var recentBefore = initial.Recent;

        MetricsFeed.Step(initial, DateTimeOffset.UnixEpoch.AddSeconds(1));

        // Copy-on-write: the snapshot a reader already holds is untouched by the next tick.
        Assert.Same(recentBefore, initial.Recent);
        Assert.Single(initial.Recent);
    }

    [Fact]
    public void Step_HistoryStaysCappedAt60()
    {
        var snapshot = MetricsFeed.CreateInitialSnapshot(DateTimeOffset.UnixEpoch);

        for (var i = 0; i < 200; i++)
        {
            snapshot = MetricsFeed.Step(snapshot, DateTimeOffset.UnixEpoch.AddSeconds(i + 1));
            Assert.True(snapshot.Recent.Count <= 60);
        }

        Assert.Equal(60, snapshot.Recent.Count);
        // Oldest rolled off: the buffer holds the 60 most-recent ticks, last one is Current.
        Assert.Equal(snapshot.Current, snapshot.Recent[^1]);
    }

    [Fact]
    public void Step_KeepsCpuWithinBounds()
    {
        var snapshot = MetricsFeed.CreateInitialSnapshot(DateTimeOffset.UnixEpoch);

        for (var i = 0; i < 500; i++)
        {
            snapshot = MetricsFeed.Step(snapshot, DateTimeOffset.UnixEpoch.AddSeconds(i + 1));
            Assert.InRange(snapshot.Current.CpuPercent, 2, 99);
            Assert.InRange(snapshot.Current.ActiveJobs, 0, 24);
        }
    }

    [Fact]
    public async Task Loop_RaisesUpdated_AndAdvancesTick()
    {
        await using var feed = new MetricsFeed(intervalMs: 10);
        var raised = 0;
        feed.Updated += () => Interlocked.Increment(ref raised);

        var startTick = feed.State.Current.Tick;
        await WaitFor.True(
            () => feed.State.Current.Tick > startTick && Volatile.Read(ref raised) > 0,
            TimeSpan.FromSeconds(2),
            "the background loop never ticked / raised Updated");

        Assert.True(feed.State.Current.Tick > startTick);
        Assert.True(Volatile.Read(ref raised) > 0);
    }

    [Fact]
    public async Task DisposeAsync_StopsTheLoop()
    {
        var feed = new MetricsFeed(intervalMs: 10);
        await WaitFor.True(
            () => feed.State.Current.Tick > 0, TimeSpan.FromSeconds(2),
            "the loop never started ticking");

        await feed.DisposeAsync();
        var tickAtDispose = feed.State.Current.Tick;

        // Well over a few intervals — if the loop were still running the tick would climb.
        await Task.Delay(80);
        Assert.Equal(tickAtDispose, feed.State.Current.Tick);
    }
}
