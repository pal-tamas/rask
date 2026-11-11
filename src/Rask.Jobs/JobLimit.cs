namespace Rask.Background;

/// <summary>
///     When one type of job stops by itself: a time limit (fixed, or computed from the job) and how long it may report no
///     progress. Unset parts are no limit. Declared in code by <see cref="JobsOptions.Limit{TJob}" />, or in configuration under
///     <c>Rask:Jobs:Limit:&lt;TypeName&gt;</c>; code wins, part by part.
/// </summary>
internal sealed class JobLimit
{
    public TimeSpan? After { get; set; }

    public Func<IJob, TimeSpan>? AfterOf { get; set; }

    public TimeSpan? IdleFor { get; set; }

    /// <summary>The time limit of <paramref name="job" />, if this rule has one.</summary>
    public TimeSpan? AfterFor(IJob job) => AfterOf is { } of ? of(job) : After;

    /// <summary>Whether this rule says anything about the time limit.</summary>
    public bool LimitsTime => After is not null || AfterOf is not null;

    /// <summary>Takes what <paramref name="other" /> says, part by part: what it leaves unset stays as it is here.</summary>
    public void Take(JobLimit other)
    {
        if (other.LimitsTime)
        {
            After = other.After;
            AfterOf = other.AfterOf;
        }

        if (other.IdleFor is { } idle)
        {
            IdleFor = idle;
        }
    }
}
