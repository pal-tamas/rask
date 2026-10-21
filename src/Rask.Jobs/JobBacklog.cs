using System.ComponentModel;
using Microsoft.EntityFrameworkCore;

namespace Rask.Background;

/// <summary>
/// The default <see cref="IJobs"/>: writes one <see cref="Job"/> row through the app's
/// <see cref="IDbContextFactory{TContext}"/>. The write is its own transaction — a job is explicitly
/// enqueued, not derived from a business change — so if you need a job to commit atomically with that
/// change, raise a domain event and deliver it with <c>Rask.Outbox</c> instead.
/// </summary>
/// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the jobs table.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class JobBacklog<TContext>(IDbContextFactory<TContext> contextFactory, TimeProvider timeProvider) : IJobs
    where TContext : DbContext
{
    /// <inheritdoc/>
    public async Task Add(IJob job, DateTimeOffset? at, TimeSpan? after, string? lane, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        var (type, payload) = JobSerializerRegistry.Serialize(job);
        var db = await Open(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);
        db.Set<Job>().Add(Job.For(type, payload, Due(at, after), lane));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     A look first, so the everyday skip costs one indexed read and no failed write; then the insert, which the
    ///     unique index on <see cref="Job.WaitingKey"/> refuses when another host wrote the same work in between.
    /// </remarks>
    public async Task<bool> AddUnlessWaiting(
        IJob job, DateTimeOffset? at, TimeSpan? after, string? lane, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        var (type, payload) = JobSerializerRegistry.Serialize(job);
        var row = Job.Waiting(type, payload, Due(at, after), lane);

        var db = await Open(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);
        return !await Waits(db, row.WaitingKey!, cancellationToken).ConfigureAwait(false)
            && await Insert(db, row, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The insert alone, as a host that lost the race between its look and its write makes it.</summary>
    internal async Task<bool> Insert(IJob job, DateTime due, string? lane, CancellationToken cancellationToken)
    {
        var (type, payload) = JobSerializerRegistry.Serialize(job);
        var db = await Open(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);
        return await Insert(db, Job.Waiting(type, payload, due, lane), cancellationToken).ConfigureAwait(false);
    }

    // Writes the waiting row, or answers false when the unique index refused it because the same work waits already.
    // Inside a transaction, EF's own savepoint undoes the refused insert alone.
    private static async Task<bool> Insert(TContext db, Job row, CancellationToken cancellationToken)
    {
        db.Set<Job>().Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException)
        {
            // Read back rather than parsed: a provider's error says "unique", and only the table says it was this key.
            db.ChangeTracker.Clear();
            if (await Waits(db, row.WaitingKey!, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            throw;
        }
    }

    // The one place an enqueue opens its context.
    private Task<TContext> Open(CancellationToken cancellationToken) => contextFactory.CreateDbContextAsync(cancellationToken);

    private static Task<bool> Waits(TContext db, string key, CancellationToken cancellationToken) =>
        db.Set<Job>().AsNoTracking().AnyAsync(j => j.WaitingKey == key, cancellationToken);

    private DateTime Due(DateTimeOffset? at, TimeSpan? after) =>
        (at ?? timeProvider.GetUtcNow() + (after ?? TimeSpan.Zero)).UtcDateTime;
}
