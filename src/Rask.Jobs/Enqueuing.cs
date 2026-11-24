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
    private readonly string? _sameWork;
    private readonly string? _notice;
    private readonly bool _shared;
    private readonly CancellationToken _cancellationToken;

    internal Enqueuing(IJobs? jobs, IJob job, DateTimeOffset? runAt, TimeSpan? delay, string? lane, CancellationToken cancellationToken)
        : this(jobs, job, runAt, delay, lane, unlessWaiting: false, sameWork: null, notice: null, shared: false, cancellationToken)
    {
    }

    private Enqueuing(
        IJobs? jobs,
        IJob job,
        DateTimeOffset? runAt,
        TimeSpan? delay,
        string? lane,
        bool unlessWaiting,
        string? sameWork,
        string? notice,
        bool shared,
        CancellationToken cancellationToken)
    {
        _jobs = jobs;
        _job = job ?? throw new ArgumentNullException(nameof(job));
        _runAt = runAt;
        _delay = delay;
        _lane = lane;
        _unlessWaiting = unlessWaiting;
        _sameWork = sameWork;
        _notice = notice;
        _shared = shared;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Runs it no earlier than <paramref name="delay"/> from now: <c>.In(24.Hours)</c>.</summary>
    public Enqueuing In(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "In cannot be negative — a job cannot run in the past.");
        }

        return new Enqueuing(_jobs, _job, null, delay, _lane, _unlessWaiting, _sameWork, _notice, _shared, _cancellationToken);
    }

    /// <summary>Runs it no earlier than <paramref name="moment"/>: <c>.At(midnight)</c>.</summary>
    public Enqueuing At(DateTimeOffset moment) => new(_jobs, _job, moment, null, _lane, _unlessWaiting, _sameWork, _notice, _shared, _cancellationToken);

    /// <summary>
    ///     Runs the job in a lane: jobs of one lane run one at a time, in the order they are due,
    ///     while different lanes run side by side - a lane per pump, per customer, per mailbox.
    /// </summary>
    public Enqueuing InLane(string lane)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lane);
        return new Enqueuing(_jobs, _job, _runAt, _delay, lane, _unlessWaiting, _sameWork, _notice, _shared, _cancellationToken);
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
    public Enqueuing UnlessWaiting() => new(_jobs, _job, _runAt, _delay, _lane, unlessWaiting: true, sameWork: null, _notice, _shared, _cancellationToken);

    /// <summary>
    ///     Writes nothing when work of the same <paramref name="key" /> already waits in this lane, unclaimed:
    ///     <c>await Jobs.Enqueue(new Redistribute(company, Current.UserId)).UnlessWaiting(company.ToString());</c> — what
    ///     <see cref="UnlessWaiting()" /> does, with the key naming the work in place of the whole payload.
    /// </summary>
    /// <remarks>
    ///     "The same work" is then the same lane, tenant, job kind and key: a job that carries who asked, or when, is still
    ///     the same as the one already waiting for the same company. Two kinds of job never share a key.
    /// </remarks>
    /// <param name="key">What names the work — at most 1,024 characters.</param>
    public Enqueuing UnlessWaiting(string key) =>
        new(_jobs, _job, _runAt, _delay, _lane, unlessWaiting: true, JobSameWork.Checked(key), _notice, _shared, _cancellationToken);

    /// <summary>
    ///     Runs the job in its tenant's lane: one at a time with the tenant's other work of <paramref name="lane" />, while
    ///     every other tenant's runs beside it — <c>await Jobs.Enqueue(new ImportFile(file.Id)).InTenantLane("loads");</c>.
    ///     <c>Jobs.Lane(Jobs.TenantLane("loads"))</c> reads it.
    /// </summary>
    /// <param name="lane">Which of the tenant's lanes, or null for its one lane.</param>
    /// <exception cref="InvalidOperationException">No tenant is in flight.</exception>
    public Enqueuing InTenantLane(string? lane = null) =>
        new(_jobs, _job, _runAt, _delay, TenantLanes.Of(lane, "Jobs.Enqueue(job).InTenantLane()"), _unlessWaiting, _sameWork, _notice, _shared, _cancellationToken);

    /// <summary>
    ///     Shows the job to whoever enqueues it, under <paramref name="title" />:
    ///     <c>await Jobs.Enqueue(new ImportPrices(file.Id)).Noticed($"Import of {file.Name}");</c>. Their <c>Ui.WorkNotices</c>
    ///     bell and <c>Ui.WorkList</c> show it while it waits and runs — with the bar its <c>Jobs.Report(done, of)</c> moves —
    ///     and say when it is done, warned, failed or stopped. Nobody else sees it — unless <see cref="ByTenant" /> shares it
    ///     — in no other tenant; a job enqueued by nobody signed in is shown to nobody.
    /// </summary>
    /// <param name="title">What the work is, in words for the person who started it — at most 256 characters.</param>
    public Enqueuing Noticed(string title) =>
        new(_jobs, _job, _runAt, _delay, _lane, _unlessWaiting, _sameWork, JobNotice.Checked(title), _shared, _cancellationToken);

    /// <summary>
    ///     Shows the noticed job to its tenant's other users too — <c>.Noticed($"Import of {file.Name}").ByTenant()</c> — those who
    ///     may run it themselves: who passes its handler's <c>[Authorize]</c>. Never to another tenant, nor to nobody signed in.
    ///     Unmarked, a job is its enqueuer's alone.
    /// </summary>
    /// <remarks>
    ///     Awaiting it throws when there is nothing to share or nobody to share it with: no <c>.Noticed(…)</c>, or no tenant in
    ///     flight where it is enqueued.
    /// </remarks>
    public Enqueuing ByTenant() => new(_jobs, _job, _runAt, _delay, _lane, _unlessWaiting, _sameWork, _notice, shared: true, _cancellationToken);

    /// <summary>Runs it: <see langword="true" /> when a row was written, <see langword="false" /> when <c>UnlessWaiting</c> skipped it.</summary>
    public TaskAwaiter<bool> GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Runs it, choosing whether the continuation returns to the captured context.</summary>
    public ConfiguredTaskAwaitable<bool> ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Runs it, as a <see cref="Task{TResult}"/> of whether a row was written.</summary>
    public Task<bool> AsTask()
    {
        if (_shared)
        {
            JobNotice.RefuseUnshareable("Jobs.Enqueue(job)", noticed: _notice is not null);
        }

        var jobs = _jobs ?? Jobs.Resolve();
        var cancellationToken = Ambient.Or(_cancellationToken);

        // Ambient while the write starts: the write's own awaits carry it on to where the row is built, and it ends here.
        var notice = JobNotice.Use(_notice, _shared);
        var sameWork = JobSameWork.Use(_sameWork);
        try
        {
            return _unlessWaiting
                ? jobs.AddUnlessWaiting(_job, _runAt, _delay, _lane, cancellationToken)
                : Added(jobs.Add(_job, _runAt, _delay, _lane, cancellationToken));
        }
        finally
        {
            sameWork.Dispose();
            notice.Dispose();
        }
    }

    private static async Task<bool> Added(Task add)
    {
        await add.ConfigureAwait(false);
        return true;
    }
}
