namespace Rask.Tests.Shared;

/// <summary>
///     A <see cref="TimeProvider" /> a test moves by hand: its wall clock stands still until <see cref="Advance" />
///     moves it or <see cref="UtcNow" /> sets it, so expiry, staleness and stamps are asserted rather than waited for.
/// </summary>
/// <remarks>
///     <para>
///         Its timers and timestamps are the system's until the clock is made with <c>holdTimers: true</c> or a test
///         calls <see cref="HoldTimers" /> — so a poll loop under test keeps ticking on its real (short) interval.
///         Held, the clock is wholly the test's: a timer made from then on fires only when <see cref="Advance" />
///         reaches it, and timestamps move with the wall clock, so a deadline of seconds runs out exactly when the
///         test says and costs it nothing (#1067). Hold the timers before anything reads a timestamp.
///     </para>
///     <para>
///         NOT the ambient clock: code that reads <c>Clock.Now</c> is driven by <c>Rask.Testing</c>'s <c>ClockFake</c>.
///         Hand-written rather than <c>Microsoft.Extensions.TimeProvider.Testing</c>'s <c>FakeTimeProvider</c>: that
///         package versions on its own monthly train, and <c>PackagePinFamilyTests</c> holds every
///         <c>Microsoft.Extensions.*</c> pin to the platform stack's single version.
///     </para>
/// </remarks>
public sealed class ManualClock(DateTimeOffset? start = null, bool holdTimers = false) : TimeProvider
{
    /// <summary>Where a clock starts when the test names no time: 2026-01-01, UTC.</summary>
    public static readonly DateTimeOffset DefaultStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private readonly List<HeldTimer> _scheduled = [];
    private DateTimeOffset _now = start ?? DefaultStart;
    private bool _holdTimers = holdTimers;

    /// <summary>The zone <see cref="TimeProvider.LocalTimeZone" /> answers; UTC unless the test names one.</summary>
    public TimeZoneInfo Zone { get; init; } = TimeZoneInfo.Utc;

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => Zone;

    /// <summary>
    ///     The clock's time. Setting it forward is an <see cref="Advance" /> (held timers that fall due fire); setting it
    ///     back only moves the clock.
    /// </summary>
    public DateTimeOffset UtcNow
    {
        get => GetUtcNow();
        set
        {
            var by = value - GetUtcNow();
            if (by >= TimeSpan.Zero)
            {
                Advance(by);
                return;
            }

            lock (_gate)
            {
                _now = value;
            }
        }
    }

    /// <summary>How many held timers are waiting to fire — a disposed or spent one is not.</summary>
    public int PendingTimers
    {
        get
        {
            lock (_gate)
            {
                return _scheduled.Count;
            }
        }
    }

    /// <inheritdoc />
    public override long TimestampFrequency => HoldsTimers ? TimeSpan.TicksPerSecond : base.TimestampFrequency;

    private bool HoldsTimers
    {
        get
        {
            lock (_gate)
            {
                return _holdTimers;
            }
        }
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    /// <inheritdoc />
    public override long GetTimestamp() => HoldsTimers ? GetUtcNow().UtcTicks : base.GetTimestamp();

    /// <summary>Makes every timer made from now on fire only when <see cref="Advance" /> reaches it.</summary>
    /// <returns>This clock, for a test that holds it as it makes it.</returns>
    public ManualClock HoldTimers()
    {
        lock (_gate)
        {
            _holdTimers = true;
        }

        return this;
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!HoldsTimers)
        {
            return base.CreateTimer(callback, state, dueTime, period);
        }

        var timer = new HeldTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock forward by <paramref name="by" />, firing every held timer that falls due.</summary>
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
            HeldTimer? next = null;
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

    private sealed class HeldTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
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
