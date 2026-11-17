using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rask.Background;

/// <summary>
///     A <c>Run</c> still being worded: <c>var done = await Jobs.Run(new AtPump(pump.Id)).InLane(pump.Id);</c>. Nothing is
///     written until it is awaited; the await stores the job, waits for it to run and hands back what its handler answered.
/// </summary>
/// <typeparam name="TResult">What the job's handler answers.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Running<TResult>
{
    private readonly IJobs? _jobs;
    private readonly IJob<TResult> _job;
    private readonly string? _lane;
    private readonly TimeSpan? _within;
    private readonly string? _notice;
    private readonly CancellationToken _cancellationToken;

    internal Running(IJobs? jobs, IJob<TResult> job, string? lane, TimeSpan? within, CancellationToken cancellationToken)
        : this(jobs, job, lane, within, notice: null, cancellationToken)
    {
    }

    private Running(IJobs? jobs, IJob<TResult> job, string? lane, TimeSpan? within, string? notice, CancellationToken cancellationToken)
    {
        _jobs = jobs;
        _job = job ?? throw new ArgumentNullException(nameof(job));
        _lane = lane;
        _within = within;
        _notice = notice;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    ///     Runs the job in a lane: one at a time with every other job of that lane, on every host, while different lanes run
    ///     side by side — a lane per pump, per customer, per mailbox.
    /// </summary>
    public Running<TResult> InLane(string lane)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lane);
        return new Running<TResult>(_jobs, _job, lane, _within, _notice, _cancellationToken);
    }

    /// <summary>
    ///     Waits at most <paramref name="wait"/> for the answer, then throws <see cref="TimeoutException"/>: <c>.Within(2.Minutes)</c>.
    ///     It bounds the WAIT, not the job, which still runs — and keeps its answer on its row — with nobody awaiting it.
    /// </summary>
    public Running<TResult> Within(TimeSpan wait)
    {
        Running.RefuseNonPositive(wait);
        return new Running<TResult>(_jobs, _job, _lane, wait, _notice, _cancellationToken);
    }

    /// <summary>
    ///     Shows the job to whoever runs it, under <paramref name="title" />, while it waits and runs, and when it ends — in their
    ///     <c>Ui.WorkNotices</c> bell and <c>Ui.WorkList</c>, as <see cref="Enqueuing.Noticed" /> does for an enqueued job.
    /// </summary>
    /// <param name="title">What the work is, in words for the person who started it — at most 256 characters.</param>
    public Running<TResult> Noticed(string title) =>
        new(_jobs, _job, _lane, _within, JobNotice.Checked(title), _cancellationToken);

    /// <summary>Runs it and waits for its answer.</summary>
    public TaskAwaiter<TResult> GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Runs it, choosing whether the continuation returns to the captured context.</summary>
    public ConfiguredTaskAwaitable<TResult> ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Runs it, as a <see cref="Task{TResult}"/> of its answer.</summary>
    public Task<TResult> AsTask()
    {
        JobInFlight.RefuseAwaitingOwnLane(_lane);

        // Ambient while the write starts: the write's own awaits carry it on to where the row is built, and it ends here.
        var notice = JobNotice.Use(_notice);
        try
        {
            return (_jobs ?? Jobs.Resolve()).Run(_job, _lane, _within, Ambient.Or(_cancellationToken));
        }
        finally
        {
            notice.Dispose();
        }
    }
}
