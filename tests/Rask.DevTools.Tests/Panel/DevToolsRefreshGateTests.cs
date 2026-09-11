using Rask.DevTools.Panel;

namespace Rask.DevTools.Tests.Panel;

/// <summary>
///     A burst of feed notifications becomes one panel refresh per interval. The interval is a task each test ends by
///     hand, and the test then awaits the refresh the gate scheduled, so nothing here waits on a clock.
/// </summary>
public sealed class DevToolsRefreshGateTests
{
    [Fact]
    public async Task A_burst_of_notifications_refreshes_once_when_the_interval_ends()
    {
        var interval = new ManualInterval();
        var refreshes = 0;
        var gate = new DevToolsRefreshGate(() => Interlocked.Increment(ref refreshes), CancellationToken.None, interval.Wait);

        for (var i = 0; i < 50; i++)
        {
            gate.Notify();
        }

        Assert.Equal(1, interval.Started);
        Assert.Equal(0, refreshes);

        await EndAndAwaitRefresh(gate, interval);

        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task A_notification_after_a_refresh_schedules_the_next_one()
    {
        var interval = new ManualInterval();
        var refreshes = 0;
        var gate = new DevToolsRefreshGate(() => Interlocked.Increment(ref refreshes), CancellationToken.None, interval.Wait);

        gate.Notify();
        await EndAndAwaitRefresh(gate, interval);
        gate.Notify();

        Assert.Equal(2, interval.Started);

        await EndAndAwaitRefresh(gate, interval);

        Assert.Equal(2, refreshes);
    }

    [Fact]
    public async Task A_notification_that_arrives_during_the_refresh_is_not_folded_into_it()
    {
        var interval = new ManualInterval();
        DevToolsRefreshGate? gate = null;
        var refreshes = 0;
        gate = new DevToolsRefreshGate(
            () =>
            {
                // A frame recorded while the panel renders: that render has already read the feed, so it needs its own.
                if (Interlocked.Increment(ref refreshes) == 1)
                {
                    gate!.Notify();
                }
            },
            CancellationToken.None,
            interval.Wait);

        gate.Notify();
        await EndAndAwaitRefresh(gate, interval);
        await EndAndAwaitRefresh(gate, interval);

        Assert.Equal(2, refreshes);
    }

    [Fact]
    public async Task No_refresh_runs_once_the_panel_is_gone()
    {
        var interval = new ManualInterval();
        using var lifetime = new CancellationTokenSource();
        var refreshes = 0;
        var gate = new DevToolsRefreshGate(() => Interlocked.Increment(ref refreshes), lifetime.Token, interval.Wait);

        gate.Notify();
        await lifetime.CancelAsync();
        await EndAndAwaitRefresh(gate, interval);
        gate.Notify();

        Assert.Equal(0, refreshes);
        Assert.Equal(1, interval.Started);
    }

    /// <summary>Ends the oldest interval and waits for the refresh that was waiting on it.</summary>
    private static Task EndAndAwaitRefresh(DevToolsRefreshGate gate, ManualInterval interval)
    {
        var refresh = gate.Scheduled;
        interval.End();
        return refresh.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Intervals that end when the test says so, oldest first.</summary>
    private sealed class ManualInterval
    {
        private readonly Queue<TaskCompletionSource> _pending = new();
        private readonly Lock _gate = new();
        private int _started;

        public int Started => Volatile.Read(ref _started);

        public Task Wait(CancellationToken cancellationToken)
        {
            var interval = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _pending.Enqueue(interval);
                _started++;
            }

            return interval.Task;
        }

        public void End()
        {
            TaskCompletionSource interval;
            lock (_gate)
            {
                interval = _pending.Dequeue();
            }

            interval.SetResult();
        }
    }
}
