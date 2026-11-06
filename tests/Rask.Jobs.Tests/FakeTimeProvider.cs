namespace Rask.Background.Tests;

/// <summary>
/// A hand-rolled fake clock (no external package): the processor's due/backoff checks read it, so tests drive time
/// deterministically while the poll loop ticks on the real (short) interval. Its timers are real too, unless a test
/// calls <see cref="HoldTimers"/>: from then on a timer made on this clock fires only when <see cref="Advance"/>
/// moves the clock past it, so a deadline such as <c>.Within(…)</c> runs out exactly when the test says.
/// </summary>
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<HeldTimer> _held = [];
    private long _ticks = start.UtcTicks;
    private bool _holdTimers;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    /// <summary>How many held timers are waiting to fire.</summary>
    public int PendingTimers
    {
        get
        {
            lock (_gate)
            {
                return _held.Count(t => t.DueAt is not null);
            }
        }
    }

    /// <summary>Makes every timer made from now on fire only when <see cref="Advance"/> reaches it.</summary>
    public void HoldTimers()
    {
        lock (_gate)
        {
            _holdTimers = true;
        }
    }

    public void Advance(TimeSpan by)
    {
        List<HeldTimer> due;
        lock (_gate)
        {
            var now = Interlocked.Add(ref _ticks, by.Ticks);
            due = [.. _held.Where(t => t.DueAt <= now)];
            foreach (var timer in due)
            {
                timer.Reschedule();
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            if (!_holdTimers)
            {
                return base.CreateTimer(callback, state, dueTime, period);
            }

            var timer = new HeldTimer(this, callback, state);
            _held.Add(timer);
            timer.Schedule(dueTime, period);
            return timer;
        }
    }

    private sealed class HeldTimer(FakeTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        private long? _period;

        public long? DueAt { get; private set; }

        public void Schedule(TimeSpan dueTime, TimeSpan period)
        {
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : Interlocked.Read(ref clock._ticks) + dueTime.Ticks;
            _period = period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero ? null : period.Ticks;
        }

        public void Reschedule() => DueAt = _period is { } period ? DueAt + period : null;

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                Schedule(dueTime, period);
                return clock._held.Contains(this);
            }
        }

        public void Dispose()
        {
            lock (clock._gate)
            {
                clock._held.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
