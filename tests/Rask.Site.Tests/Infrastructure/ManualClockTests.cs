namespace Rask.Site.Tests.Infrastructure;

// The retry tests trust this clock to fire exactly what a real one would, in the same order, so it is held
// to that here rather than only through the tests that lean on it.
public sealed class ManualClockTests
{
    [Fact]
    public void ATimerFiresOnlyOnceTheClockReachesIt()
    {
        var clock = new ManualClock();
        var fired = 0;
        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(0, fired);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, fired);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, fired);
    }

    [Fact]
    public void ATimerACallbackCreatesIsTimedFromThenAndFiresInTheSameAdvance()
    {
        var clock = new ManualClock();
        var start = clock.GetUtcNow();
        var firedAt = new List<TimeSpan>();
        ITimer? second = null;
        using var first = clock.CreateTimer(
            _ =>
            {
                firedAt.Add(clock.GetUtcNow() - start);
                second = clock.CreateTimer(
                    _ => firedAt.Add(clock.GetUtcNow() - start), null, TimeSpan.FromMilliseconds(150),
                    Timeout.InfiniteTimeSpan);
            },
            null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal<TimeSpan>([TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1150)], firedAt);
        Assert.Equal(start + TimeSpan.FromSeconds(2), clock.GetUtcNow());
        second?.Dispose();
    }

    [Fact]
    public void ADisposedTimerNeverFires()
    {
        var clock = new ManualClock();
        var fired = false;
        var timer = clock.CreateTimer(_ => fired = true, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        timer.Dispose();
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.False(fired);
        Assert.False(timer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan));
    }

    [Fact]
    public void TaskDelayAndACancellationDeadlineCompleteInsideAdvance()
    {
        // The two BCL waits HttpFetchDemo builds on the injected clock.
        var clock = new ManualClock();
        var delay = Task.Delay(TimeSpan.FromMilliseconds(150), clock);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), clock);

        Assert.False(delay.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.True(delay.IsCompletedSuccessfully);

        Assert.False(deadline.IsCancellationRequested);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(deadline.IsCancellationRequested);
    }
}
