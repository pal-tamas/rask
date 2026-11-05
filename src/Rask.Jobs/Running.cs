using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rask.Background;

/// <summary>
///     A <c>Run</c> of a job that answers nothing, still being worded: <c>await Jobs.Run(new Calibrate(pump.Id)).InLane(pump.Id);</c>.
///     Nothing is written until it is awaited; the await stores the job and returns once it has run.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Running
{
    private readonly IJobs? _jobs;
    private readonly IJob _job;
    private readonly string? _lane;
    private readonly TimeSpan? _within;
    private readonly CancellationToken _cancellationToken;

    internal Running(IJobs? jobs, IJob job, string? lane, TimeSpan? within, CancellationToken cancellationToken)
    {
        _jobs = jobs;
        _job = job ?? throw new ArgumentNullException(nameof(job));
        _lane = lane;
        _within = within;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    ///     Runs the job in a lane: one at a time with every other job of that lane, on every host, while different lanes run
    ///     side by side — a lane per pump, per customer, per mailbox.
    /// </summary>
    public Running InLane(string lane)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lane);
        return new Running(_jobs, _job, lane, _within, _cancellationToken);
    }

    /// <summary>
    ///     Waits at most <paramref name="wait"/> for the job to run, then throws <see cref="TimeoutException"/>: <c>.Within(2.Minutes)</c>.
    ///     It bounds the WAIT, not the job, which still runs with nobody awaiting it.
    /// </summary>
    public Running Within(TimeSpan wait)
    {
        RefuseNonPositive(wait);
        return new Running(_jobs, _job, _lane, wait, _cancellationToken);
    }

    /// <summary>Runs it and waits until it has run.</summary>
    public TaskAwaiter GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Runs it, choosing whether the continuation returns to the captured context.</summary>
    public ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Runs it, as a <see cref="Task"/> that ends when the job has.</summary>
    public Task AsTask()
    {
        JobInFlight.RefuseAwaitingOwnLane(_lane);
        return (_jobs ?? Jobs.Resolve()).Run(_job, _lane, _within, Ambient.Or(_cancellationToken));
    }

    internal static void RefuseNonPositive(TimeSpan wait)
    {
        if (wait <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(wait), wait, "Within must be longer than zero — nothing can run in no time.");
        }
    }
}
