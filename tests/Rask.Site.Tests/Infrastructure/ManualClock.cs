namespace Rask.Site.Tests.Infrastructure;

/// <summary>
///     A <see cref="TimeProvider" /> that moves only when a test calls <see cref="Advance" />, firing the timers
///     that fall due on the calling thread.
/// </summary>
/// <remarks>
///     <para>
///         For code whose waits are the thing under test — a retry delay, a per-attempt deadline — so the test
///         advances time instead of sleeping through it, and a deadline of seconds costs the test nothing (#1067).
///     </para>
///     <para>
///         Hand-written rather than <c>Microsoft.Extensions.TimeProvider.Testing</c>'s <c>FakeTimeProvider</c>:
///         that package versions on its own monthly train, and <c>PackagePinFamilyTests</c> holds every
///         <c>Microsoft.Extensions.*</c> pin to the platform stack's single version.
///     </para>
/// </remarks>
internal sealed class ManualClock : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _scheduled = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock forward by <paramref name="by" />, firing every timer that falls due.</summary>
    /// <remarks>
    ///     Timers fire one at a time in due order, with the clock set to each one's due time as it fires. A timer
    ///     a callback creates is therefore timed from the moment it was created, and fires within this same call
    ///     if it falls due before the end, as a callback that schedules its own follow-up expects.
    /// </remarks>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);

        DateTimeOffset end;
        lock (_gate)
        {
            end = _now + by;
        }

        while (true)
        {
            ManualTimer? next = null;
            lock (_gate)
            {
                foreach (var timer in _scheduled)
                {
                    if (timer.DueAt <= end && (next is null || timer.DueAt < next.DueAt))
                    {
                        next = timer;
                    }
                }

                if (next is null)
                {
                    _now = end;
                    return;
                }

                if (next.DueAt > _now)
                {
                    _now = next.DueAt;
                }

                next.Rearm();
            }

            // Outside the lock: a callback reads the clock and creates timers of its own.
            next.Fire();
        }
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period;
        private bool _disposed;

        public DateTimeOffset DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                if (_disposed)
                {
                    return false;
                }

                clock._scheduled.Remove(this);
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    DueAt = clock._now + dueTime;
                    _period = period;
                    clock._scheduled.Add(this);
                }

                return true;
            }
        }

        // Under the clock's lock, just before firing: a periodic timer moves to its next due time and a one-shot
        // timer leaves the schedule. A period of zero or Infinite is a one-shot, as it is for Threading.Timer.
        public void Rearm()
        {
            if (_period > TimeSpan.Zero)
            {
                DueAt += _period;
            }
            else
            {
                clock._scheduled.Remove(this);
            }
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (clock._gate)
            {
                _disposed = true;
                clock._scheduled.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
