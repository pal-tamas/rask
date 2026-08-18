using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Rask.Data;

/// <summary>
/// Loads many new entities in one go. Entity Framework Core answers the bulk <i>update</i> and <i>delete</i>
/// shapes with <c>ExecuteUpdate</c>/<c>ExecuteDelete</c>, but its own plan puts bulk <i>inserts</i> out of
/// scope, so seeding, importing and migrating data is left to every application to hand-roll. This is that
/// code, written once.
/// </summary>
/// <remarks>
/// <para>
/// The insert runs through the context, so everything Rask.Data wires stays true: the
/// <see cref="AuditingInterceptor"/> stamps <see cref="ITimestamped.CreatedAt"/>/<see cref="ITimestamped.UpdatedAt"/>
/// and the <see cref="DomainEventInterceptor"/> publishes each entity's domain events, exactly as they would
/// for an ordinary save. What changes is the shape of the work: the entities are added and saved in batches
/// of <see cref="BulkInsertOptions.BatchSize"/>, change detection is off for the duration, and the change
/// tracker is cleared between batches — which is what keeps a large load flat in memory rather than
/// quadratic.
/// </para>
/// <para>
/// Each batch commits on its own unless <see cref="BulkInsertOptions.SingleTransaction"/> asks otherwise;
/// see that property for why per-batch is the default on a single-writer database.
/// </para>
/// <para>
/// The context must have no pending changes: the load clears the change tracker as it goes, so unsaved work
/// would be discarded rather than silently swept into the first batch. Save or discard it first.
/// </para>
/// </remarks>
public static class BulkInsertExtensions
{
    /// <summary>
    /// Inserts <paramref name="entities"/> in batches.
    /// </summary>
    /// <typeparam name="TEntity">The entity type, mapped on <paramref name="context"/>.</typeparam>
    /// <param name="context">The context to insert through.</param>
    /// <param name="entities">The new entities. Nothing is inserted for an empty sequence.</param>
    /// <param name="configure">
    /// Overrides for the defaults — <see cref="BulkInsertOptions.BatchSize"/> and
    /// <see cref="BulkInsertOptions.SingleTransaction"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The number of rows written.</returns>
    /// <exception cref="InvalidOperationException">
    /// The context has unsaved changes, or the load runs inside a transaction while the entities carry
    /// domain events (which would be published before that transaction commits).
    /// </exception>
    public static async Task<int> BulkInsertAsync<TEntity>(
        this DbContext context,
        IEnumerable<TEntity> entities,
        Action<BulkInsertOptions>? configure = null,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);

        var options = new BulkInsertOptions();
        configure?.Invoke(options);
        options.Validate();

        // Entries() runs DetectChanges first, so this sees uncommitted work the caller never saved. Clearing
        // the tracker between batches would throw it away, so refuse rather than lose it.
        if (context.ChangeTracker.Entries().Any(static e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "BulkInsertAsync needs a context with no pending changes — it clears the change tracker " +
                "between batches, which would discard them. Call SaveChangesAsync (or ChangeTracker.Clear) first.");
        }

        // Inside a transaction every batch's SaveChanges runs before the commit — and DomainEventInterceptor
        // publishes in SavedChanges. A load that failed later would already have announced rows that rolled
        // back, so refuse the combination rather than ship an event nobody can take back.
        var enclosed = options.SingleTransaction || context.Database.CurrentTransaction is not null;

        // A single transaction is one retryable unit, so a retrying strategy re-runs the load from the top
        // and the sequence has to survive a second enumeration. Buffer a lazy one; leave a materialised
        // collection alone. The guard below needs a second pass for the same reason.
        var source = enclosed && entities is not IReadOnlyCollection<TEntity>
            ? entities.ToList()
            : entities;

        if (enclosed)
        {
            GuardAgainstDomainEvents(source);
        }

        if (!options.SingleTransaction)
        {
            // No transaction of our own: each batch's SaveChanges is its own unit, and EF applies the
            // configured execution strategy to it. Wrapping the whole loop instead would replay already
            // committed batches on a retry.
            return await InsertBatchesAsync(context, source, options, cancellationToken).ConfigureAwait(false);
        }

        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(
            source,
            (ctx, state, token) => InsertInOneTransactionAsync(ctx, state, options, token),
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts <paramref name="entities"/> in batches — the <see cref="DbSet{TEntity}"/> spelling of
    /// <see cref="BulkInsertAsync{TEntity}(DbContext, IEnumerable{TEntity}, Action{BulkInsertOptions}?, CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="set">The set to insert into.</param>
    /// <param name="entities">The new entities. Nothing is inserted for an empty sequence.</param>
    /// <param name="configure">Overrides for the defaults.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The number of rows written.</returns>
    public static Task<int> BulkInsertAsync<TEntity>(
        this DbSet<TEntity> set,
        IEnumerable<TEntity> entities,
        Action<BulkInsertOptions>? configure = null,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(set);

        var context = set.GetService<ICurrentDbContext>().Context;
        return context.BulkInsertAsync(entities, configure, cancellationToken);
    }

    private static async Task<int> InsertInOneTransactionAsync<TEntity>(
        DbContext context,
        IEnumerable<TEntity> entities,
        BulkInsertOptions options,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        // Join the caller's transaction when there is one, so a bulk insert composes with surrounding work;
        // own one otherwise, so the load is all-or-nothing.
        var ambient = context.Database.CurrentTransaction;
        var transaction = ambient is null
            ? await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        try
        {
            var written = await InsertBatchesAsync(context, entities, options, cancellationToken).ConfigureAwait(false);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return written;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static void GuardAgainstDomainEvents<TEntity>(IEnumerable<TEntity> entities)
        where TEntity : class
    {
        if (!typeof(IHasDomainEvents).IsAssignableFrom(typeof(TEntity)))
        {
            return;
        }

        if (entities.OfType<IHasDomainEvents>().Any(static e => e.DomainEvents.Count > 0))
        {
            throw new InvalidOperationException(
                "BulkInsertAsync cannot run inside a transaction while the entities carry domain events: " +
                "DomainEventInterceptor publishes in SavedChanges, which inside a transaction happens before " +
                "the commit, so a later failure would leave events published for rows that rolled back. " +
                "Either drop SingleTransaction (and any enclosing transaction), clear the events, or use " +
                "Rask.Outbox, whose messages are written in the same transaction and drained after it commits.");
        }
    }

    private static async Task<int> InsertBatchesAsync<TEntity>(
        DbContext context,
        IEnumerable<TEntity> entities,
        BulkInsertOptions options,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var tracker = context.ChangeTracker;
        var autoDetectChanges = tracker.AutoDetectChangesEnabled;

        // Nothing tracked here was loaded from the database or mutated by the caller — every entity is a
        // fresh Added, so scanning the graph for changes on each save is pure cost.
        tracker.AutoDetectChangesEnabled = false;

        try
        {
            var written = 0;
            foreach (var batch in entities.Chunk(options.BatchSize))
            {
                context.AddRange(batch);
                written += await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                // Holding the batch tracked would make the next batch's save walk it again, which is the
                // quadratic cost this method exists to avoid.
                tracker.Clear();
            }

            return written;
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }
}
