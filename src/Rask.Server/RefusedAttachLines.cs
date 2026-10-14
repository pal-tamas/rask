namespace Rask.Server;

/// <summary>How many refused attaches are written to the log: a handful a window, however many are refused.</summary>
/// <remarks>
///     A refused attach costs its sender nothing and can be repeated without end with any id. Every one is refused;
///     only the first few of each ten seconds are logged, which is enough to tell an operator it is happening. Each
///     host keeps its own window, read on its store's clock.
/// </remarks>
internal sealed class RefusedAttachLines(Func<TimeProvider> clock)
{
    internal const int PerWindow = 20;

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private long _windowStart = long.MinValue;
    private int _written;

    /// <summary>Whether one more line may be written now.</summary>
    internal bool Admit()
    {
        var time = clock();
        var now = time.GetTimestamp();
        var start = Volatile.Read(ref _windowStart);
        if (start == long.MinValue || time.GetElapsedTime(start, now) >= Window)
        {
            Volatile.Write(ref _windowStart, now);
            Volatile.Write(ref _written, 0);
        }

        return Interlocked.Increment(ref _written) <= PerWindow;
    }
}
