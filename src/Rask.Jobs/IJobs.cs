using System.ComponentModel;

namespace Rask.Background;

/// <summary>
/// The app's job queue. Reach it without injecting anything — <c>await Jobs.Enqueue(job)</c> — or inject
/// this and word the same sentence: <c>await jobs.Enqueue(job).In(24.Hours)</c>.
/// </summary>
public interface IJobs
{
    /// <summary>
    /// Writes one job row, due at <paramref name="at"/>, or <paramref name="after"/> from now, or now when
    /// neither is given. The primitive under <c>Enqueue</c>; call <c>Enqueue(job)</c> and its
    /// <c>In</c>/<c>At</c> steps instead.
    /// </summary>
    /// <remarks>
    /// <paramref name="after"/> arrives as a duration rather than an instant because "now" is the queue's to
    /// decide: it holds the app's <see cref="TimeProvider"/>, which is what a test moves, and a caller that
    /// resolved the delay itself would enqueue against a clock the processor never reads.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Task Add(IJob job, DateTimeOffset? at, TimeSpan? after, string? lane, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes one job row as <see cref="Add"/> does — unless the same work already waits in <paramref name="lane"/>,
    /// unclaimed. The primitive under <c>.UnlessWaiting()</c>; call that step instead.
    /// </summary>
    /// <returns><see langword="true"/> when the row was written, <see langword="false"/> when it was skipped.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Task<bool> AddUnlessWaiting(IJob job, DateTimeOffset? at, TimeSpan? after, string? lane, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes one job row a caller awaits, in <paramref name="lane"/>, and waits — at most <paramref name="within"/>, when
    /// given — until it has run, answering what its handler returned. The primitive under <c>Jobs.Run(job)</c>; call that.
    /// </summary>
    /// <exception cref="JobFailedException">The job failed; it is not retried.</exception>
    /// <exception cref="TimeoutException"><paramref name="within"/> went by first; the job still runs.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Task<TResult> Run<TResult>(IJob<TResult> job, string? lane, TimeSpan? within, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes one job row a caller awaits, as <see cref="Run{TResult}"/> does, for a job that answers nothing. The primitive
    /// under <c>Jobs.Run(job)</c>; call that.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Task Run(IJob job, string? lane, TimeSpan? within, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops job <paramref name="id"/>: a running job is cancelled and ends stopped, a waiting one never runs. The primitive
    /// under <c>Jobs.Stop(id)</c>; call that.
    /// </summary>
    /// <returns><see langword="false"/> when the job is done already, stopped already, or does not exist.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Task<bool> Stop(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The state of <paramref name="lanes"/>, in one read: every lane asked for is in the answer, idle or not. The primitive
    /// under <c>Jobs.Lanes(…)</c> and <c>Jobs.Lane(…)</c>; call those.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Task<IReadOnlyDictionary<string, JobLane>> Lanes(IEnumerable<string> lanes, CancellationToken cancellationToken = default);
}
