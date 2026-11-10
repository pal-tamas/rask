using System.Collections.Concurrent;

namespace Rask.Background;

/// <summary>
///     What the queue and the processor of one app say to each other in process, so an awaited job (<c>Jobs.Run</c>) is
///     neither claimed nor read back a whole poll late: the queue wakes the processor when it writes one, and the processor
///     says when it has saved one's outcome. Only a shortcut — the row is the truth, and a caller on another host, which
///     hears neither, reads it on every poll.
/// </summary>
internal sealed class JobSignals
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource> _finished = new();
    private readonly ConcurrentDictionary<long, JobRun> _runs = new();
    private TaskCompletionSource _wake = NewLatch();

    /// <summary>Completes on the next <see cref="Wake"/>. Taken before a poll, so a wake during it is not missed.</summary>
    public Task Woken => Volatile.Read(ref _wake).Task;

    /// <summary>Asks the processor to poll now rather than at its next tick.</summary>
    public void Wake() => Interlocked.Exchange(ref _wake, NewLatch()).TrySetResult();

    /// <summary>Completes when this process has saved the outcome of job <paramref name="id"/>.</summary>
    public Task Finished(long id) => _finished.GetOrAdd(id, static _ => NewLatch()).Task;

    /// <summary>Stops listening for job <paramref name="id"/>: its caller has its answer, or gave up waiting.</summary>
    public void Forget(long id) => _finished.TryRemove(id, out _);

    /// <summary>Says the outcome of job <paramref name="id"/> is saved, to a caller in this process awaiting it.</summary>
    public void Saved(long id)
    {
        if (_finished.TryRemove(id, out var latch))
        {
            latch.TrySetResult();
        }
    }

    private static TaskCompletionSource NewLatch() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Job <paramref name="run" /> is in its handler on this host — until its outcome is saved (<see cref="Over" />).</summary>
    public void Began(JobRun run) => _runs[run.Id] = run;

    /// <summary>Job <paramref name="id" />'s outcome is saved, or left to whoever ended it: it is no longer this host's.</summary>
    public void Over(long id) => _runs.TryRemove(id, out _);

    /// <summary>
    ///     Job <paramref name="id" /> as this host runs it, or null when it runs nowhere here: what a stop asked on this host
    ///     cancels at once, and what tells a lane's view that work whose row is still claimed has ended here.
    /// </summary>
    public JobRun? Run(long id) => _runs.GetValueOrDefault(id);
}
