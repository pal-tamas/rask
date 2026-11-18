namespace Rask.Background;

/// <summary>
///     A watched job's row as its enqueuer sees it (<see cref="WorkReported" />): where it stands, read from the row alone —
///     what the processor says once it has saved an outcome, and what the bell's board reads.
/// </summary>
internal static class JobWork
{
    /// <summary>Where <paramref name="job" /> stands: ended done, warned, refused or stopped, failed for good, or still to run.</summary>
    /// <param name="job">The row.</param>
    /// <param name="maxAttempts">How many attempts make a failure final.</param>
    /// <param name="now">The processor's now (UTC): a claim that has not run out is a run still in a handler.</param>
    public static WorkState StateOf(Job job, int maxAttempts, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.ProcessedAt is not null)
        {
            return job.Stopped is not null ? WorkState.Stopped : job.Outcome ?? WorkState.Done;
        }

        // A dead letter: every attempt spent, the last one failed, and no attempt in a handler now.
        var running = job.ClaimedUntil > now;
        return job.Attempts >= maxAttempts && job.Error is not null && !running ? WorkState.Failed : WorkState.Running;
    }

    /// <summary><paramref name="job" /> as its enqueuer sees it, <paramref name="state" />, as of <paramref name="at" />.</summary>
    public static WorkReported Of(Job job, WorkState state, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(job);
        return new WorkReported(job.Id, job.Notice ?? "", job.Progress, job.Done, job.Of, state, at)
        {
            Batch = job.Batch,
            Detail = state == WorkState.Running ? null : job.Detail,
            Owner = job.UserId,
            Tenant = job.TenantId,
            Lane = job.Lane,
            Shared = job.Shared,
            Kind = job.Type,
        };
    }

    /// <summary>Whether its enqueuer watches <paramref name="job" />: it has a title, and somebody to show it to.</summary>
    public static bool IsNoticed(Job job) => job is { Notice: not null, UserId: not null };
}
