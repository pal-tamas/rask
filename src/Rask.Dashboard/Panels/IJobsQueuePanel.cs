namespace Rask.Dashboard.Panels;

/// <summary>
/// The jobs queue: the one queue that says what runs right now, in which lane, and can stop a job. Mail and the outbox
/// have nothing to stop, so this is not on <see cref="IQueueActions"/>.
/// </summary>
internal interface IJobsQueuePanel : IQueuePanel
{
    /// <summary>
    /// Stops job <paramref name="id"/> (<c>Jobs.Stop</c>): a running job is cancelled and ends stopped, a waiting one never
    /// runs. False when it had already ended.
    /// </summary>
    Task<bool> Stop(long id, CancellationToken cancellationToken);
}
