using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rask.Background;

/// <summary>
///     An <c>Enqueue</c> still being worded: <c>await Jobs.Enqueue(job).In(24.Hours)</c>. Nothing is
///     written until it is awaited, and the await says whether a row was written.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Enqueuing
{
    private readonly IJobs? _jobs;
    private readonly IJob _job;
    private readonly DateTimeOffset? _runAt;
    private readonly TimeSpan? _delay;
    private readonly string? _lane;
    private readonly bool _unlessWaiting;
    private readonly CancellationToken _cancellationToken;

    internal Enqueuing(IJobs? jobs, IJob job, DateTimeOffset? runAt, TimeSpan? delay, string? lane, CancellationToken cancellationToken)
        : this(jobs, job, runAt, delay, lane, unlessWaiting: false, cancellationToken)
    {
    }

    private Enqueuing(
        IJobs? jobs, IJob job, DateTimeOffset? runAt, TimeSpan? delay, string? lane, bool unlessWaiting, CancellationToken cancellationToken)
    {
        _jobs = jobs;
        _job = job ?? throw new ArgumentNullException(nameof(job));
        _runAt = runAt;
        _delay = delay;
        _lane = lane;
        _unlessWaiting = unlessWaiting;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Runs it no earlier than <paramref name="delay"/> from now: <c>.In(24.Hours)</c>.</summary>
    public Enqueuing In(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "In cannot be negative — a job cannot run in the past.");
        }

        return new Enqueuing(_jobs, _job, null, delay, _lane, _unlessWaiting, _cancellationToken);
    }

    /// <summary>Runs it no earlier than <paramref name="moment"/>: <c>.At(midnight)</c>.</summary>
    public Enqueuing At(DateTimeOffset moment) => new(_jobs, _job, moment, null, _lane, _unlessWaiting, _cancellationToken);

    /// <summary>
    ///     Runs the job in a lane: jobs of one lane run one at a time, in the order they are due,
    ///     while different lanes run side by side - a lane per pump, per customer, per mailbox.
    /// </summary>
    public Enqueuing InLane(string lane)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lane);
        return new Enqueuing(_jobs, _job, _runAt, _delay, lane, _unlessWaiting, _cancellationToken);
    }

    /// <summary>
    ///     Writes nothing when the same work already waits in this lane and has not been claimed yet:
    ///     <c>var asked = await Jobs.Enqueue(new AskStatus(pump.Id)).InLane(pump.Id).UnlessWaiting();</c> is
    ///     <see langword="false" /> when it was skipped.
    /// </summary>
    /// <remarks>
    ///     "The same work" is the same lane, tenant, job kind and serialized payload. A copy that has been claimed —
    ///     running, or waiting for a retry — does not count, so a new round can wait behind a running one. The job
    ///     table decides, through a unique index, so two hosts enqueueing at once still leave one waiting row.
    /// </remarks>
    public Enqueuing UnlessWaiting() => new(_jobs, _job, _runAt, _delay, _lane, unlessWaiting: true, _cancellationToken);

    /// <summary>Runs it: <see langword="true" /> when a row was written, <see langword="false" /> when <c>UnlessWaiting</c> skipped it.</summary>
    public TaskAwaiter<bool> GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Runs it, choosing whether the continuation returns to the captured context.</summary>
    public ConfiguredTaskAwaitable<bool> ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Runs it, as a <see cref="Task{TResult}"/> of whether a row was written.</summary>
    public Task<bool> AsTask()
    {
        var jobs = _jobs ?? Jobs.Resolve();
        var cancellationToken = Ambient.Or(_cancellationToken);
        return _unlessWaiting
            ? jobs.AddUnlessWaiting(_job, _runAt, _delay, _lane, cancellationToken)
            : Added(jobs.Add(_job, _runAt, _delay, _lane, cancellationToken));
    }

    private static async Task<bool> Added(Task add)
    {
        await add.ConfigureAwait(false);
        return true;
    }
}
