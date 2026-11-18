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
    private readonly string? _notice;
    private readonly bool _shared;
    private readonly CancellationToken _cancellationToken;

    internal Running(IJobs? jobs, IJob job, string? lane, TimeSpan? within, CancellationToken cancellationToken)
        : this(jobs, job, lane, within, notice: null, shared: false, cancellationToken)
    {
    }

    private Running(IJobs? jobs, IJob job, string? lane, TimeSpan? within, string? notice, bool shared, CancellationToken cancellationToken)
    {
        _jobs = jobs;
        _job = job ?? throw new ArgumentNullException(nameof(job));
        _lane = lane;
        _within = within;
        _notice = notice;
        _shared = shared;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    ///     Runs the job in a lane: one at a time with every other job of that lane, on every host, while different lanes run
    ///     side by side — a lane per pump, per customer, per mailbox.
    /// </summary>
    public Running InLane(string lane)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lane);
        return new Running(_jobs, _job, lane, _within, _notice, _shared, _cancellationToken);
    }

    /// <summary>
    ///     Waits at most <paramref name="wait"/> for the job to run, then throws <see cref="TimeoutException"/>: <c>.Within(2.Minutes)</c>.
    ///     It bounds the WAIT, not the job, which still runs with nobody awaiting it.
    /// </summary>
    public Running Within(TimeSpan wait)
    {
        RefuseNonPositive(wait);
        return new Running(_jobs, _job, _lane, wait, _notice, _shared, _cancellationToken);
    }

    /// <summary>
    ///     Shows the job to whoever runs it, under <paramref name="title" />, while it waits and runs, and when it ends — in their
    ///     <c>Ui.WorkNotices</c> bell and <c>Ui.WorkList</c>, as <see cref="Enqueuing.Noticed" /> does for an enqueued job.
    /// </summary>
    /// <param name="title">What the work is, in words for the person who started it — at most 256 characters.</param>
    public Running Noticed(string title) =>
        new(_jobs, _job, _lane, _within, JobNotice.Checked(title), _shared, _cancellationToken);

    /// <summary>
    ///     Shows the noticed job to its tenant's other users too, those who pass its handler's <c>[Authorize]</c> — as
    ///     <see cref="Enqueuing.ByTenant" /> does for an enqueued job. Awaiting it throws with no <c>.Noticed(…)</c> or no tenant.
    /// </summary>
    public Running ByTenant() => new(_jobs, _job, _lane, _within, _notice, shared: true, _cancellationToken);

    /// <summary>Runs it and waits until it has run.</summary>
    public TaskAwaiter GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Runs it, choosing whether the continuation returns to the captured context.</summary>
    public ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Runs it, as a <see cref="Task"/> that ends when the job has.</summary>
    public Task AsTask()
    {
        JobInFlight.RefuseAwaitingOwnLane(_lane);

        // Ambient while the write starts: the write's own awaits carry it on to where the row is built, and it ends here.
        if (_shared)
        {
            JobNotice.RefuseUnshareable("Jobs.Run(job)", noticed: _notice is not null);
        }

        var notice = JobNotice.Use(_notice, _shared);
        try
        {
            return (_jobs ?? Jobs.Resolve()).Run(_job, _lane, _within, Ambient.Or(_cancellationToken));
        }
        finally
        {
            notice.Dispose();
        }
    }

    internal static void RefuseNonPositive(TimeSpan wait)
    {
        if (wait <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(wait), wait, "Within must be longer than zero — nothing can run in no time.");
        }
    }
}
