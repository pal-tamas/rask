namespace Rask.Core.Live;

/// <summary>
///     How many events ran nothing because the handler they were sent to was gone, or could not be shown to be the
///     same one, by the time they arrived. Process-wide, read by the host's metrics.
/// </summary>
internal static class StaleEvents
{
    private static long _missed;

    /// <summary>The count so far.</summary>
    internal static long Missed => Interlocked.Read(ref _missed);

    internal static void CountMissed() => Interlocked.Increment(ref _missed);
}
