using System.ComponentModel;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     The persistence half of the writes on every model — <c>Product.CreateAsync(…)</c>,
///     <c>Product.UpdateAsync(id, …)</c> and <c>Product.DeleteAsync(id)</c>.
/// </summary>
/// <remarks>
///     <para>
///         Called by generated code, which lives in the application's assembly and so cannot reach an
///         <c>internal</c> member. Not an API to write against: call the members on the model type.
///     </para>
///     <para>
///         Each write makes one change through the change tracker — so the auditing, soft-delete and
///         domain-event interceptors all see it — and saves. Without a context it opens one and disposes it
///         afterwards; handed one, it works in that context, saves it, and leaves it open, so the write joins
///         whatever transaction the caller started there.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedModelWrites
{
    /// <summary>Inserts <paramref name="entity" />.</summary>
    /// <param name="entity">The entity to insert.</param>
    /// <param name="db">
    ///     The context to insert through, or <c>null</c> to open one. A given context is saved — with anything
    ///     else pending on it — and is not disposed.
    /// </param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The inserted entity, with any store-generated key filled in.</returns>
    public static Task<TEntity> CreateAsync<TEntity>(
        TEntity entity,
        DbContext? db = null,
        CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(entity);

        return InContextAsync(db, async context =>
        {
            context.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return entity;
        });
    }

    /// <summary>
    ///     Loads the row with <paramref name="key" />, applies <paramref name="apply" /> to it and saves —
    ///     so only the columns whose values actually changed are written.
    /// </summary>
    /// <param name="key">The primary key of the row to update.</param>
    /// <param name="version">
    ///     The <see cref="Columns.Version" /> the caller's copy was read at, or <c>null</c> to skip the
    ///     optimistic-concurrency check.
    /// </param>
    /// <param name="apply">Sets the caller's values on the loaded entity.</param>
    /// <param name="db">
    ///     The context to load and save through, or <c>null</c> to open one. A given context is saved — with
    ///     anything else pending on it — and is not disposed; a row it already tracks is the one updated.
    /// </param>
    /// <param name="cancellationToken">Cancels the load and the save.</param>
    /// <returns>The updated entity.</returns>
    /// <exception cref="KeyNotFoundException">No row has <paramref name="key" /> (or it is soft-deleted).</exception>
    /// <exception cref="DbUpdateConcurrencyException">The row's version is no longer <paramref name="version" />.</exception>
    public static Task<TEntity> UpdateAsync<TEntity>(
        object key,
        int? version,
        Action<TEntity> apply,
        DbContext? db = null,
        CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(apply);

        return InContextAsync(db, async context =>
        {
            var entity = await LoadAsync<TEntity>(context, key, cancellationToken).ConfigureAwait(false);

            ExpectVersion(context, entity, version);
            apply(entity);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return entity;
        });
    }

    /// <summary>
    ///     Deletes the row with <paramref name="key" /> through the change tracker, so an
    ///     <see cref="Aggregate{TId}" /> is stamped rather than removed.
    /// </summary>
    /// <param name="key">The primary key of the row to delete.</param>
    /// <param name="version">
    ///     The <see cref="Columns.Version" /> the caller last saw, or <c>null</c> to delete whatever the
    ///     current version is.
    /// </param>
    /// <param name="db">
    ///     The context to load and save through, or <c>null</c> to open one. A given context is saved — with
    ///     anything else pending on it — and is not disposed.
    /// </param>
    /// <param name="cancellationToken">Cancels the load and the save.</param>
    /// <exception cref="KeyNotFoundException">No row has <paramref name="key" /> (or it is soft-deleted).</exception>
    /// <exception cref="DbUpdateConcurrencyException">The row's version is no longer <paramref name="version" />.</exception>
    public static Task DeleteAsync<TEntity>(
        object key,
        int? version,
        DbContext? db = null,
        CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(key);

        return InContextAsync(db, async context =>
        {
            var entity = await LoadAsync<TEntity>(context, key, cancellationToken).ConfigureAwait(false);

            ExpectVersion(context, entity, version);
            context.Remove(entity);

            return await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        });
    }

    // The one place the "given or owned" rule lives: a caller's context is theirs to dispose, one opened here
    // is disposed here — including when the write throws.
    private static async Task<TResult> InContextAsync<TResult>(DbContext? db, Func<DbContext, Task<TResult>> write)
    {
        if (db is not null)
        {
            return await write(db).ConfigureAwait(false);
        }

        await using var context = Db.CreateContext();
        return await write(context).ConfigureAwait(false);
    }

    // Tracked, and whole: a write that applied a change to a collection which had not been loaded would see an
    // empty one and sync it away. The read path loads by key the same way, so both halves of "load one root"
    // agree about what a root is.
    private static async Task<TEntity> LoadAsync<TEntity>(DbContext context, object key, CancellationToken cancellationToken)
        where TEntity : class, IAggregate
    {
        var entity = await context.Set<TEntity>().FindAsync([key], cancellationToken).ConfigureAwait(false)
                     ?? throw new KeyNotFoundException(
                         $"There is no {typeof(TEntity).Name} with key '{key}' — it was never created, or it has " +
                         "been deleted since it was read.");

        await AggregateChildren
            .LoadChildrenAsync(context, entity, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return entity;
    }

    // The save's WHERE clause compares against the ORIGINAL value of a concurrency token, and a freshly
    // loaded row's original is whatever the database holds now — which would make every check pass. Pinning
    // the original to the version the caller read is what turns "someone saved since" into an exception.
    private static void ExpectVersion(DbContext context, IAggregate entity, int? version)
    {
        if (version is not { } expected)
        {
            return;
        }

        context.Entry(entity).Property(Columns.Version).OriginalValue = expected;
    }
}
