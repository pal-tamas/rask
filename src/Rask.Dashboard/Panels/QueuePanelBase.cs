using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Rask.Dashboard.Panels;

/// <summary>
/// The counting, filtering and paging every queue panel shares, so there is a single definition of what
/// "failed" means rather than three that can drift.
/// <para>
/// The predicates are written with <see cref="EF.Property{T}" /> against the three columns the outbox,
/// jobs and mail tables have in common — <c>ProcessedAt</c>, <c>Attempts</c>, and a run-time column each
/// adapter names. That is what allows one predicate to serve three entity types: filtering has to happen
/// on the entity, because EF cannot compose a <c>Where</c> onto a projection into a non-entity type.
/// </para>
/// </summary>
/// <typeparam name="TContext">The application context that owns the table.</typeparam>
/// <typeparam name="TEntity">The battery's entity type.</typeparam>
internal abstract class QueuePanelBase<TContext, TEntity>(IDbContextFactory<TContext> contextFactory, TimeProvider timeProvider)
    : IQueuePanel
    where TContext : DbContext
    where TEntity : class
{
    private bool? _mapped;

    public abstract string Slug { get; }

    public abstract string Title { get; }

    public abstract UiIconName Icon { get; }

    public abstract int MaxAttempts { get; }

    /// <summary><c>true</c> when the battery's <c>AddRaskX</c> ran — probed via its options singleton.</summary>
    protected abstract bool IsRegistered { get; }

    /// <summary>
    /// The column holding "not eligible before this time" — <c>RunAt</c> for jobs and mail. The outbox has
    /// no such column and names <c>OccurredAt</c> here, which correctly leaves its Delayed count at zero.
    /// </summary>
    protected abstract string RunAtProperty { get; }

    /// <summary>Maps the entity onto the shared row shape. Applied after filtering, never before.</summary>
    protected abstract Expression<Func<TEntity, QueueRow>> Projection { get; }

    /// <summary>
    /// Registered AND mapped. Both halves matter: a package reference without
    /// <c>modelBuilder.AddRaskX()</c> leaves the service registered but every query throwing, which is
    /// exactly the state a dashboard should report as "not here" rather than crash on.
    /// </summary>
    public bool IsAvailable => IsRegistered && IsMapped();

    public async Task<QueueCounts> CountsAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return default;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Counted on the entity set — no projection needed just to count rows.
        return new QueueCounts(
            Due: await CountAsync(db, QueueFilter.Due, now, cancellationToken).ConfigureAwait(false),
            Delayed: await CountAsync(db, QueueFilter.Delayed, now, cancellationToken).ConfigureAwait(false),
            Failed: await CountAsync(db, QueueFilter.Failed, now, cancellationToken).ConfigureAwait(false),
            Processed: await CountAsync(db, QueueFilter.Processed, now, cancellationToken).ConfigureAwait(false));
    }

    public async Task<(IReadOnlyList<QueueRow> Rows, int Total)> PageAsync(
        QueueFilter filter, int skip, int take, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return ([], 0);
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var filtered = Filter(db.Set<TEntity>(), filter, now);

        var total = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await Order(filtered, filter)
            .Skip(skip)
            .Take(take)
            .Select(Projection)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (rows, total);
    }

    public Task<int> RetryAsync(long id, CancellationToken cancellationToken) =>
        RetryWhereAsync(e => EF.Property<long>(e, "Id") == id, cancellationToken);

    public Task<int> RetryAllAsync(CancellationToken cancellationToken) =>
        RetryWhereAsync(_ => true, cancellationToken);

    public async Task<int> PurgeProcessedAsync(TimeSpan olderThan, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return 0;
        }

        var cutoff = timeProvider.GetUtcNow().UtcDateTime - olderThan;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // ProcessedAt IS NOT NULL is the whole guard: outstanding work and dead letters are both
        // untouched, whatever cutoff the caller passes.
        return await db.Set<TEntity>()
            .Where(e => EF.Property<DateTime?>(e, "ProcessedAt") != null
                        && EF.Property<DateTime?>(e, "ProcessedAt") < cutoff)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return 0;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Outstanding only. Deleting a processed row would erase the record of work that actually
        // happened, which is the one thing an operator can never undo.
        return await db.Set<TEntity>()
            .Where(e => EF.Property<long>(e, "Id") == id && EF.Property<DateTime?>(e, "ProcessedAt") == null)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // The retry guard is the inverse of the drain query, so it can only ever match rows the processor has
    // already given up on — a row in flight is invisible to it, which is what makes this safe to run
    // against a live queue with no coordination.
    private async Task<int> RetryWhereAsync(
        Expression<Func<TEntity, bool>> scope, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return 0;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var max = MaxAttempts;
        var runAt = RunAtProperty;

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<TEntity>()
            .Where(scope)
            .Where(e => EF.Property<DateTime?>(e, "ProcessedAt") == null && EF.Property<int>(e, "Attempts") >= max)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => EF.Property<int>(e, "Attempts"), 0)
                    .SetProperty(e => EF.Property<DateTime>(e, runAt), now)
                    .SetProperty(e => EF.Property<string?>(e, "Error"), (string?)null),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<int> CountAsync(TContext db, QueueFilter filter, DateTime now, CancellationToken ct) =>
        Filter(db.Set<TEntity>(), filter, now).CountAsync(ct);

    // The one definition of each slice. "Failed" is the inverse of the processors' own drain predicate
    // (ProcessedAt == null && Attempts < MaxAttempts && RunAt <= now) — a row they will never take again.
    private IQueryable<TEntity> Filter(IQueryable<TEntity> rows, QueueFilter filter, DateTime now)
    {
        var runAt = RunAtProperty;
        var max = MaxAttempts;

        return filter switch
        {
            QueueFilter.Due => rows.Where(e =>
                EF.Property<DateTime?>(e, "ProcessedAt") == null
                && EF.Property<int>(e, "Attempts") < max
                && EF.Property<DateTime>(e, runAt) <= now),
            QueueFilter.Delayed => rows.Where(e =>
                EF.Property<DateTime?>(e, "ProcessedAt") == null
                && EF.Property<int>(e, "Attempts") < max
                && EF.Property<DateTime>(e, runAt) > now),
            QueueFilter.Failed => rows.Where(e =>
                EF.Property<DateTime?>(e, "ProcessedAt") == null
                && EF.Property<int>(e, "Attempts") >= max),
            QueueFilter.Processed => rows.Where(e => EF.Property<DateTime?>(e, "ProcessedAt") != null),
            _ => rows.Where(e => EF.Property<DateTime?>(e, "ProcessedAt") == null),
        };
    }

    // Outstanding work reads best in the order the processor will actually take it; finished and
    // given-up work reads best newest-first, because that's where you look after something broke.
    private IQueryable<TEntity> Order(IQueryable<TEntity> rows, QueueFilter filter)
    {
        var runAt = RunAtProperty;
        return filter is QueueFilter.Processed or QueueFilter.Failed
            ? rows.OrderByDescending(e => EF.Property<long>(e, "Id"))
            : rows.OrderBy(e => EF.Property<DateTime>(e, runAt)).ThenBy(e => EF.Property<long>(e, "Id"));
    }

    // EF builds the model once per app and caches it, so this is a dictionary lookup after the first call;
    // cached per panel instance anyway to keep it off the render path.
    private bool IsMapped()
    {
        if (_mapped is { } known)
        {
            return known;
        }

        using var db = contextFactory.CreateDbContext();
        var mapped = db.Model.FindEntityType(typeof(TEntity)) is not null;
        _mapped = mapped;
        return mapped;
    }
}
