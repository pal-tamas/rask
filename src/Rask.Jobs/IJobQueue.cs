namespace Rask.Jobs;

/// <summary>
/// Enqueues background jobs onto the app's database. Inject it and call <see cref="EnqueueAsync"/> to run a
/// job as soon as the processor next polls, or <see cref="ScheduleAsync(IJob, TimeSpan, CancellationToken)"/>
/// to run it later.
/// </summary>
public interface IJobQueue
{
    /// <summary>Enqueues a job to run as soon as possible.</summary>
    Task EnqueueAsync(IJob job, CancellationToken cancellationToken = default);

    /// <summary>Enqueues a job to run no earlier than <paramref name="delay"/> from now.</summary>
    Task ScheduleAsync(IJob job, TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>Enqueues a job to run no earlier than <paramref name="runAt"/>.</summary>
    Task ScheduleAsync(IJob job, DateTimeOffset runAt, CancellationToken cancellationToken = default);
}
