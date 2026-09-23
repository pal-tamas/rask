using System.ComponentModel;
using Microsoft.EntityFrameworkCore;

namespace Rask.Jobs;

/// <summary>
/// The default <see cref="IJobs"/>: writes one <see cref="Job"/> row through the app's
/// <see cref="IDbContextFactory{TContext}"/>. The write is its own transaction — a job is explicitly
/// enqueued, not derived from a business change — so if you need a job to commit atomically with that
/// change, raise a domain event and deliver it with <c>Rask.Outbox</c> instead.
/// </summary>
/// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the jobs table.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class JobQueue<TContext>(IDbContextFactory<TContext> contextFactory, TimeProvider timeProvider) : IJobs
    where TContext : DbContext
{
    /// <inheritdoc/>
    public async Task Add(IJob job, DateTimeOffset? at, TimeSpan? after, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        var due = (at ?? timeProvider.GetUtcNow() + (after ?? TimeSpan.Zero)).UtcDateTime;
        var (type, payload) = JobSerializerRegistry.Serialize(job);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Set<Job>().Add(Job.For(type, payload, due));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
