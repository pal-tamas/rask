using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>A clock that moves only when a pause is taken on it — so a test counts a wait's pauses instead of sleeping them.</summary>
internal sealed class SteppedClock : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<TimeSpan> _pauses = [];
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public IReadOnlyList<TimeSpan> Pauses
    {
        get
        {
            lock (_gate)
            {
                return [.. _pauses];
            }
        }
    }

    public TimeSpan Elapsed => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) + Elapsed;

    /// <summary>Spend <paramref name="pause"/> at once: the clock moves on by it and it is recorded.</summary>
    public Task Pause(TimeSpan pause, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _pauses.Add(pause);
        }

        Interlocked.Add(ref _ticks, pause.Ticks);
        return Task.CompletedTask;
    }

    public ReadinessPoll Poll(TimeSpan interval, TimeSpan timeout) =>
        new() { Interval = interval, Timeout = timeout, Clock = this, Sleep = Pause };
}
