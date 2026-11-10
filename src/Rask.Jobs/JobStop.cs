namespace Rask.Background;

/// <summary>
///     Why a job was stopped before it finished: <c>Jobs.Stopped</c> inside its handler, <see cref="Job.Stopped" /> on its row,
///     <see cref="JobStoppedException.Stop" /> at a <c>Jobs.Run(job)</c> that awaited it. A stopped job has not failed: it is
///     not tried again and leaves no dead letter.
/// </summary>
public enum JobStop
{
    /// <summary>Somebody asked: <c>Jobs.Stop(id)</c>, or the dashboard's Stop.</summary>
    Asked = 1,

    /// <summary>It ran past its time limit: <c>c.Jobs.Limit&lt;TJob&gt;().After(…)</c>, or <see cref="JobsOptions.StopAfter" />.</summary>
    TimeLimit = 2,

    /// <summary>It reported no progress (<c>Jobs.Report(…)</c>) for as long as <c>c.Jobs.Limit&lt;TJob&gt;().IdleFor(…)</c>.</summary>
    Idle = 3,

    /// <summary>
    ///     Its row was marked done, or deleted, outside the processor while it ran. The row is left as whoever changed it left
    ///     it, so this reason is never written there: it is seen in <c>Jobs.Stopped</c>, the log and the metric only.
    /// </summary>
    DoneElsewhere = 4,
}
