using System.Diagnostics;

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     A clock a test moves by hand, for behaviour that depends on how old something is.
/// </summary>
/// <remarks>
///     Only the readings are manual. Timers still come from the base <see cref="TimeProvider" />, so a
///     periodic loop keeps its real cadence — what a test controls is the age a timestamp reports.
/// </remarks>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp = Stopwatch.GetTimestamp();
    private long _utcNowTicks = DateTimeOffset.UtcNow.UtcTicks;

    public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcNowTicks), TimeSpan.Zero);

    /// <summary>Moves both readings forward by <paramref name="by" />.</summary>
    public void Advance(TimeSpan by)
    {
        Interlocked.Add(ref _timestamp, (long)(by.TotalSeconds * TimestampFrequency));
        Interlocked.Add(ref _utcNowTicks, by.Ticks);
    }
}
