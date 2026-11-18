using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rask.Background;

/// <summary>
///     A <c>Jobs.Enqueue(jobs)</c> of several jobs still being worded:
///     <c>await Jobs.Enqueue(files.Select(f =&gt; new ImportFile(f.Id))).Noticed(j =&gt; $"Import of {j.Name}").ByTenant();</c>.
///     Nothing is written until it is awaited; the await writes every row in one save — all of them or none — and answers how
///     many it wrote.
/// </summary>
/// <typeparam name="TJob">The kind of job.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Enqueuing<TJob>
    where TJob : IJob
{
    private readonly IJobs? _jobs;
    private readonly IEnumerable<TJob> _batch;
    private readonly DateTimeOffset? _runAt;
    private readonly TimeSpan? _delay;
    private readonly string? _lane;
    private readonly Func<TJob, string>? _title;
    private readonly bool _shared;
    private readonly CancellationToken _cancellationToken;

    internal Enqueuing(IJobs? jobs, IEnumerable<TJob> batch, CancellationToken cancellationToken)
        : this(jobs, batch, null, null, null, null, shared: false, cancellationToken)
    {
    }

    private Enqueuing(
        IJobs? jobs,
        IEnumerable<TJob> batch,
        DateTimeOffset? runAt,
        TimeSpan? delay,
        string? lane,
        Func<TJob, string>? title,
        bool shared,
        CancellationToken cancellationToken)
    {
        _jobs = jobs;
        _batch = batch ?? throw new ArgumentNullException(nameof(batch));
        _runAt = runAt;
        _delay = delay;
        _lane = lane;
        _title = title;
        _shared = shared;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Runs them no earlier than <paramref name="delay" /> from now: <c>.In(5.Minutes)</c>.</summary>
    public Enqueuing<TJob> In(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "In cannot be negative — a job cannot run in the past.");
        }

        return new Enqueuing<TJob>(_jobs, _batch, null, delay, _lane, _title, _shared, _cancellationToken);
    }

    /// <summary>Runs them no earlier than <paramref name="moment" />: <c>.At(midnight)</c>.</summary>
    public Enqueuing<TJob> At(DateTimeOffset moment) =>
        new(_jobs, _batch, moment, null, _lane, _title, _shared, _cancellationToken);

    /// <summary>Runs them in one lane: one at a time, in the order they are given, beside every other lane.</summary>
    public Enqueuing<TJob> InLane(string lane)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lane);
        return new Enqueuing<TJob>(_jobs, _batch, _runAt, _delay, lane, _title, _shared, _cancellationToken);
    }

    /// <summary>
    ///     Shows each job to whoever enqueues them, under the title <paramref name="title" /> gives it —
    ///     <c>.Noticed(j =&gt; $"Import of {j.Name}")</c> — as one group in their <c>Ui.WorkNotices</c> bell, with one bar for
    ///     the whole batch and one toast when its last job ends.
    /// </summary>
    /// <param name="title">Each job's title, in words for the person who started it — at most 256 characters.</param>
    public Enqueuing<TJob> Noticed(Func<TJob, string> title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return new Enqueuing<TJob>(_jobs, _batch, _runAt, _delay, _lane, title, _shared, _cancellationToken);
    }

    /// <summary>
    ///     Shows the noticed batch to its tenant's other users too, those who pass its handler's <c>[Authorize]</c> — as
    ///     <see cref="Enqueuing.ByTenant" /> does for one job. Awaiting it throws with no <c>.Noticed(…)</c> or no tenant.
    /// </summary>
    public Enqueuing<TJob> ByTenant() => new(_jobs, _batch, _runAt, _delay, _lane, _title, shared: true, _cancellationToken);

    /// <summary>Writes them: how many rows were written — every job given, or none when the write fails.</summary>
    public TaskAwaiter<int> GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Writes them, choosing whether the continuation returns to the captured context.</summary>
    public ConfiguredTaskAwaitable<int> ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Writes them, as a <see cref="Task{TResult}" /> of how many rows were written.</summary>
    public Task<int> AsTask()
    {
        List<TJob> batch = [.. _batch];
        if (batch.Exists(job => job is null))
        {
            throw new InvalidOperationException("Jobs.Enqueue(jobs) was given a null job; every job of a batch is one to run.");
        }

        var title = _title;
        List<(IJob Job, string? Notice)> rows = [.. batch.Select(job => ((IJob)job, title is null ? null : JobNotice.Checked(title(job))))];
        if (_shared)
        {
            JobNotice.RefuseUnshareable("Jobs.Enqueue(jobs)", noticed: title is not null);
        }

        if (rows.Count == 0)
        {
            return Task.FromResult(0);
        }

        var jobs = _jobs ?? Jobs.Resolve();
        if (jobs is not IJobBatches batches)
        {
            throw new InvalidOperationException(
                $"{jobs.GetType().Name} cannot write a batch: Jobs.Enqueue(jobs) needs Rask.Jobs' own queue, or Jobs.Fake() in a test.");
        }

        return batches.AddAll(rows, _runAt, _delay, _lane, _shared, Ambient.Or(_cancellationToken));
    }
}
