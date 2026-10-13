namespace Rask.Server;

/// <summary>How many refused attaches are written to the log: a handful a window, however many are refused.</summary>
/// <remarks>
///     A refused attach costs its sender nothing and can be repeated without end with any id. Every one is refused;
///     only the first few of each ten seconds are logged, which is enough to tell an operator it is happening.
/// </remarks>
internal static class RefusedAttachLines
{
    internal const int PerWindow = 20;

    private const long WindowMilliseconds = 10_000;

    private static long _windowStart;
    private static int _written;

    /// <summary>Whether one more line may be written now.</summary>
    internal static bool Admit()
    {
        var now = Environment.TickCount64;
        if (now - Volatile.Read(ref _windowStart) >= WindowMilliseconds)
        {
            Volatile.Write(ref _windowStart, now);
            Volatile.Write(ref _written, 0);
        }

        return Interlocked.Increment(ref _written) <= PerWindow;
    }

    /// <summary>Lets a test start from an empty window.</summary>
    internal static void ResetForTests() => Volatile.Write(ref _windowStart, 0);
}
