using Microsoft.EntityFrameworkCore;

namespace Rask.Jobs;

/// <summary>
/// The default <see cref="IJobQueue"/>: writes one <see cref="Job"/> row through the app's
/// <see cref="IDbContextFactory{TContext}"/>. The write is its own transaction — a job is explicitly
/// enqueued, not derived from a business change — so if you need a job to commit atomically with that
/// change, raise a domain event and deliver it with <c>Rask.Outbox</c> instead.
/// </summary>
/// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the jobs table.</typeparam>
public sealed class JobQueue<TContext>(IDbContextFactory<TContext> contextFactory, TimeProvider timeProvider) : IJobQueue
    where TContext : DbContext
{
    /// <inheritdoc/>
    public Task EnqueueAsync(IJob job, CancellationToken cancellationToken = default) =>
        WriteAsync(job, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    /// <inheritdoc/>
    public Task ScheduleAsync(IJob job, TimeSpan delay, CancellationToken cancellationToken = default) =>
        WriteAsync(job, timeProvider.GetUtcNow().UtcDateTime + delay, cancellationToken);

    /// <inheritdoc/>
    public Task ScheduleAsync(IJob job, DateTimeOffset runAt, CancellationToken cancellationToken = default) =>
        WriteAsync(job, runAt.UtcDateTime, cancellationToken);

    private async Task WriteAsync(IJob job, DateTime runAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var (type, payload) = JobSerializerRegistry.Serialize(job);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Set<Job>().Add(new Job
        {
            Type = type,
            Payload = payload,
            RunAt = runAt,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
