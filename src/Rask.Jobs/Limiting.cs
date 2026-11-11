using System.ComponentModel;

namespace Rask.Background;

/// <summary>
///     The limits of one type of job, still being worded: <c>c.Jobs.Limit&lt;AtPump&gt;().After(30.Minutes).IdleFor(5.Minutes)</c>.
///     The bare <c>Limit&lt;TJob&gt;()</c> sets nothing; each step sets one limit, and the last one said wins.
/// </summary>
/// <typeparam name="TJob">The job the limits hold for.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class Limiting<TJob>
    where TJob : IJob
{
    private readonly JobLimit _rule;

    internal Limiting(JobLimit rule) => _rule = rule;

    /// <summary>
    ///     Stops a running <typeparamref name="TJob" /> once it has run this long (<see cref="JobStop.TimeLimit" />), however
    ///     much progress it reports. Counted from when its handler starts; time waiting in its lane does not count.
    /// </summary>
    /// <param name="limit">How long it may run: <c>30.Minutes</c>.</param>
    public Limiting<TJob> After(TimeSpan limit)
    {
        RefuseNonPositive(limit, nameof(limit));
        _rule.After = limit;
        _rule.AfterOf = null;
        return this;
    }

    /// <summary>
    ///     Stops a running <typeparamref name="TJob" /> after the time <paramref name="limit" /> answers for it — for work whose
    ///     length depends on what it was asked to do: <c>.After(job =&gt; job.Task == PumpTask.Status ? 1.Minute : 15.Minutes)</c>.
    /// </summary>
    /// <param name="limit">How long this job may run, read from the job itself when its handler starts.</param>
    public Limiting<TJob> After(Func<TJob, TimeSpan> limit)
    {
        ArgumentNullException.ThrowIfNull(limit);
        _rule.AfterOf = job => limit((TJob)job);
        _rule.After = null;
        return this;
    }

    /// <summary>
    ///     Stops a running <typeparamref name="TJob" /> that has reported no progress — no <c>Jobs.Report(…)</c> — for this long
    ///     (<see cref="JobStop.Idle" />). Counted from when its handler starts, and again from each report.
    /// </summary>
    /// <param name="idle">How long it may go without reporting: <c>5.Minutes</c>.</param>
    public Limiting<TJob> IdleFor(TimeSpan idle)
    {
        RefuseNonPositive(idle, nameof(idle));
        _rule.IdleFor = idle;
        return this;
    }

    private static void RefuseNonPositive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name, value, $"A limit of {typeof(TJob).Name} must be longer than zero.");
        }
    }
}
