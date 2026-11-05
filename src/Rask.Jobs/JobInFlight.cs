namespace Rask.Background;

/// <summary>
///     The lane of the job this flow is running, if it is running one — so a job that awaits another job of its own lane
///     (<c>await Jobs.Run(next).InLane(myLane)</c>) is refused instead of waiting forever: the lane runs one job at a time,
///     and the one it would wait for queues behind the one waiting.
/// </summary>
internal static class JobInFlight
{
    private static readonly AsyncLocal<Running?> Current = new();

    /// <summary>Marks this flow as running a job of <paramref name="lane"/> (null: the default lane) until disposed.</summary>
    public static IDisposable Enter(string? lane)
    {
        var previous = Current.Value;
        Current.Value = new Running(lane);
        return new Leave(previous);
    }

    /// <summary>Throws when awaiting a job of <paramref name="lane"/> here could never end.</summary>
    public static void RefuseAwaitingOwnLane(string? lane)
    {
        if (Current.Value is { } running && string.Equals(running.Lane, lane, StringComparison.Ordinal))
        {
            var which = lane is null ? "the default lane (no InLane)" : $"lane '{lane}'";
            throw new InvalidOperationException(
                $"A job of {which} cannot await another job of {which}: the lane runs one job at a time, so the one awaited "
                + "would wait for the one awaiting it. Enqueue it instead - await Jobs.Enqueue(job).InLane(lane) - or run it "
                + "in another lane.");
        }
    }

    private sealed record Running(string? Lane);

    private sealed class Leave(Running? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
