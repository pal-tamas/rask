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
///         Each write makes one change through the change tracker, so the auditing, soft-delete and
///         domain-event interceptors all see it. <b>Who saves depends on who owns the context.</b> Without one
///         the write opens its own, saves, and disposes it — the whole write is one transaction. Handed one,
///         the write only STAGES its change and returns: the caller saves, which is what makes several writes
///         on one context a single transaction with no explicit transaction to start.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedModelWrites
{
    /// <summary>Inserts <paramref name="entity" />.</summary>
    /// <param name="entity">The entity to insert.</param>
    /// <param name="db">
    ///     The context to insert through, or <c>null</c> to open one. A given context is only STAGED — the
    ///     caller saves it, and it is not disposed.
    /// </param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>
    ///     The inserted entity. Its store-generated key is filled in only once the row is saved, so with
    ///     <paramref name="db" /> given an integer key is still 0 until the caller saves; a Guid key was
    ///     assigned before the insert and is already there.
    /// </returns>
    public static Task<TEntity> CreateAsync<TEntity>(
        TEntity entity,
        DbContext? db = null,
        CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(entity);

        return InContextAsync(db, context =>
        {
            context.Add(entity);
            return Task.FromResult(entity);
        }, cancellationToken);
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
    ///     The context to load and change through, or <c>null</c> to open one. A given context is only STAGED
    ///     — the caller saves it, and it is not disposed; a row it already tracks is the one updated.
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

            return entity;
        }, cancellationToken);
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
    ///     The context to load and delete through, or <c>null</c> to open one. A given context is only STAGED
    ///     — the caller saves it, and it is not disposed.
    /// </param>
    /// <param name="cancellationToken">Cancels the load and the save.</param>
    /// <exception cref="KeyNotFoundException">No row has <paramref name="key" /> (or it is soft-deleted).</exception>
    /// <exception cref="DbUpdateConcurrencyException">The row's version is no longer <paramref name="version" />.</exception>
    /// <exception cref="InvalidOperationException">The aggregate declares <see cref="Deletion.None" />.</exception>
    public static Task DeleteAsync<TEntity>(
        object key,
        int? version,
        DbContext? db = null,
        CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(key);

        // The generated Product.DeleteAsync is not emitted for such an aggregate; this closes the direct call.
        if (ConventionRegistry.DeletesFor(typeof(TEntity)) == Deletion.None)
        {
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name} declares Deletes = Deletion.None and is never deleted; " +
                "retire it through a method of its own (Cancel, Archive) instead.");
        }

        return InContextAsync(db, async context =>
        {
            var entity = await LoadAsync<TEntity>(context, key, cancellationToken).ConfigureAwait(false);

            ExpectVersion(context, entity, version);
            context.Remove(entity);

            return entity;
        }, cancellationToken);
    }

    /// <summary>
    ///     Loads the aggregate with <paramref name="key" /> whole and untracked — what
    ///     <c>Product.ModelAsync(id)</c> fills a form from.
    /// </summary>
    /// <param name="key">The primary key of the row to load.</param>
    /// <param name="db">The context to read through, or <c>null</c> to open one. A given context is not disposed.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The aggregate and its children, or <c>null</c> when no row has that key.</returns>
    /// <remarks>
    ///     Not a read-face query: the form model keeps value objects nested and its children as child MODELS,
    ///     while the read face is flat primitives. This loads the aggregate itself so the generated fill can
    ///     be reused verbatim, which is also why there is exactly one mapping to keep right.
    /// </remarks>
    public static async Task<TEntity?> ModelSourceAsync<TEntity>(
        object key,
        DbContext? db = null,
        CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(key);

        if (db is not null)
        {
            return await AggregateLoad.FindAsync<TEntity>(db, [key], cancellationToken).ConfigureAwait(false);
        }

        await using var context = Db.CreateContext();
        return await AggregateLoad.FindAsync<TEntity>(context, [key], cancellationToken).ConfigureAwait(false);
    }

    // The one place the "given or owned" rule lives, and it decides BOTH questions: a caller's context is
    // theirs to dispose and theirs to save, one opened here is disposed and saved here — including when the
    // write throws. That is what makes `db:` mean "the caller owns the unit of work": two writes staged on one
    // context commit together under the caller's single SaveChangesAsync, with no transaction to start by hand.
    private static async Task<TResult> InContextAsync<TResult>(
        DbContext? db,
        Func<DbContext, Task<TResult>> write,
        CancellationToken cancellationToken)
    {
        if (db is not null)
        {
            return await write(db).ConfigureAwait(false);
        }

        await using var context = Db.CreateContext();
        var result = await write(context).ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return result;
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
