namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     A host clock that stands still until a test moves it, so a window measured in seconds is the test's own
///     and not however long a loaded runner took between two frames.
/// </summary>
internal sealed class StoppedClock : TimeProvider
{
    private long _ticks = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public override DateTimeOffset GetUtcNow() => new(GetTimestamp(), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
