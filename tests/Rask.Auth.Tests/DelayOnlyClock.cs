namespace Rask.Auth.Tests;

/// <summary>
///     A clock whose timestamp moves only by the one-shot delays asked of it: each fires at once, after moving the
///     clock by exactly what it asked for. A test reads off it how long the code chose to wait, whatever the machine.
/// </summary>
/// <remarks>
///     A periodic timer (a background sweep) never fires and moves nothing, so only the code under test moves the clock.
///     The wall clock (<see cref="TimeProvider.GetUtcNow" />) stays the system's.
/// </remarks>
internal sealed class DelayOnlyClock : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (period != Timeout.InfiniteTimeSpan || dueTime == Timeout.InfiniteTimeSpan)
        {
            return new Inert();
        }

        Interlocked.Add(ref _ticks, dueTime.Ticks);

        // Fired off the caller's stack: Task.Delay holds the timer only once CreateTimer has returned it.
        ThreadPool.UnsafeQueueUserWorkItem(static s => s.callback(s.state), (callback, state), preferLocal: false);
        return new Inert();
    }

    private sealed class Inert : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
