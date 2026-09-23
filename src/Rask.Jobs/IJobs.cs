using System.ComponentModel;

namespace Rask.Jobs;

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
    Task Add(IJob job, DateTimeOffset? at, TimeSpan? after, CancellationToken cancellationToken = default);
}
