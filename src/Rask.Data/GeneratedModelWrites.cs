using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     The persistence half of the writes on every model — <c>Product.Create(…)</c>,
///     <c>Product.Update(id, …)</c> and <c>Product.Delete(id)</c>.
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
///     <para>
///         An unset token is the work in progress's (<c>Current.Cancellation</c>), as it is for a read, so
///         <c>await Product.Create(model)</c> is cancelled with the request, job or dispatch it runs in.
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
    /// <param name="cancellationToken">Cancels the save; unset, the work this call belongs to does.</param>
    /// <returns>
    ///     The inserted entity. Its store-generated key is filled in only once the row is saved, so with
    ///     <paramref name="db" /> given an integer key is still 0 until the caller saves; a Guid key was
    ///     assigned before the insert and is already there.
    /// </returns>
    public static Task<TEntity> Create<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        TEntity entity,
        DbContext? db = null,
        CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        cancellationToken = Ambient.Or(cancellationToken);
        ArgumentNullException.ThrowIfNull(entity);

        return InContextAsync(db, context =>
        {
            context.Add(entity);
            return Task.FromResult(entity);
        }, cancellationToken);
    }

    /// <summary>
    ///     Saves <paramref name="entity" /> as it now stands — what <c>order.Save()</c> does: inserted when it
    ///     has no row yet, otherwise written over its row, children synced by key.
    /// </summary>
    /// <param name="entity">The aggregate, from <c>Find</c> or freshly built.</param>
    /// <param name="db">
    ///     The context to save through, or <c>null</c> to open one. A given context is only STAGED — the
    ///     caller saves it, and it is not disposed.
    /// </param>
    /// <param name="cancellationToken">Cancels the load and the save; unset, the work this call belongs to does.</param>
    /// <returns>The saved aggregate, its version and times refreshed.</returns>
    /// <exception cref="KeyNotFoundException">Its row has been soft-deleted since it was read.</exception>
    /// <exception cref="DbUpdateConcurrencyException">Its row was saved by someone else since it was read.</exception>
    public static async Task<TEntity> Save<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        TEntity entity,
        DbContext? db = null,
        CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        cancellationToken = Ambient.Or(cancellationToken);
        ArgumentNullException.ThrowIfNull(entity);

        if (db is not null)
        {
            AmbientTransaction.Admit(db);
            await AggregateSave.StageAsync(db, entity, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await AggregateSave.SaveAsync(entity, cancellationToken).ConfigureAwait(false);
        }

        return entity;
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
    /// <param name="cancellationToken">Cancels the load and the save; unset, the work this call belongs to does.</param>
    /// <returns>The updated entity.</returns>
    /// <exception cref="KeyNotFoundException">No row has <paramref name="key" /> (or it is soft-deleted).</exception>
    /// <exception cref="DbUpdateConcurrencyException">The row's version is no longer <paramref name="version" />.</exception>
    public static Task<TEntity> Update<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        object key,
        int? version,
        Action<TEntity> apply,
        DbContext? db = null,
        CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        cancellationToken = Ambient.Or(cancellationToken);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(apply);

        return InContextAsync(db, async context =>
        {
            var entity = await LoadAsync<TEntity>(context, key, cancellationToken).ConfigureAwait(false);

            ExpectVersion(context, entity, version);
            apply(entity);

            // At once, not at the save: a caller's context may load or change more before it saves. A refill
            // written `Lines = [.. rows]` left a read-only collection, which the match writes into.
            WritableChildren.Ensure(context);
            ReplacedChildren.Match(context);

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
    /// <param name="cancellationToken">Cancels the load and the save; unset, the work this call belongs to does.</param>
    /// <exception cref="KeyNotFoundException">No row has <paramref name="key" /> (or it is soft-deleted).</exception>
    /// <exception cref="DbUpdateConcurrencyException">The row's version is no longer <paramref name="version" />.</exception>
    public static Task Delete<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        object key,
        int? version,
        DbContext? db = null,
        CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        cancellationToken = Ambient.Or(cancellationToken);
        ArgumentNullException.ThrowIfNull(key);

        return InContextAsync(db, async context =>
        {
            var entity = await LoadAsync<TEntity>(context, key, cancellationToken).ConfigureAwait(false);

            ExpectVersion(context, entity, version);
            TrackedReferenceRefusal.Remove(context, entity);

            return entity;
        }, cancellationToken);
    }

    /// <summary>
    ///     Deletes the aggregate the caller holds — what <c>product.Delete()</c> does — from what <c>Find</c> read,
    ///     with no read first: a stamp when it implements <see cref="ISoftDeletable" />, the row and its children otherwise.
    /// </summary>
    /// <param name="entity">The aggregate, from <c>Find</c> or saved.</param>
    /// <param name="db">
    ///     The context to delete through, or <c>null</c> to open one. A given context is only STAGED — the caller saves
    ///     it, and it is not disposed.
    /// </param>
    /// <param name="cancellationToken">Cancels the save; unset, the work this call belongs to does.</param>
    /// <exception cref="InvalidOperationException">It was never saved.</exception>
    /// <exception cref="KeyNotFoundException">Its row has been deleted since it was read, and it has no Version to say so.</exception>
    /// <exception cref="DbUpdateConcurrencyException">Its row was changed or deleted by someone else since it was read.</exception>
    public static async Task Delete<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        TEntity entity,
        DbContext? db = null,
        CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        cancellationToken = Ambient.Or(cancellationToken);
        ArgumentNullException.ThrowIfNull(entity);

        if (db is not null)
        {
            AmbientTransaction.Admit(db);
            await AggregateSave.StageDeleteAsync(db, entity, cancellationToken).ConfigureAwait(false);
            return;
        }

        var context = Db.CreateContext();
        await using var contextScope = context.ConfigureAwait(false);
        await AggregateSave.StageDeleteAsync(context, entity, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Loads the aggregate with <paramref name="key" /> whole and untracked — what
    ///     <c>Product.Model(id)</c> fills a form from.
    /// </summary>
    /// <param name="key">The primary key of the row to load.</param>
    /// <param name="db">The context to read through, or <c>null</c> to open one. A given context is not disposed.</param>
    /// <param name="cancellationToken">Cancels the load; unset, the work this call belongs to does.</param>
    /// <returns>The aggregate and its children, or <c>null</c> when no row has that key.</returns>
    /// <remarks>
    ///     Not a read-face query: the form model keeps value objects nested and its children as child MODELS,
    ///     while the read face is flat primitives. This loads the aggregate itself so the generated fill can
    ///     be reused verbatim, which is also why there is exactly one mapping to keep right.
    /// </remarks>
    public static async Task<TEntity?> ModelSource<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        object key,
        DbContext? db = null,
        CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        cancellationToken = Ambient.Or(cancellationToken);
        ArgumentNullException.ThrowIfNull(key);

        if (db is not null)
        {
            AmbientTransaction.Admit(db);
            return await AggregateLoad.FindAsync<TEntity>(db, [key], cancellationToken).ConfigureAwait(false);
        }

        var context = Db.CreateContext();
        await using var contextScope = context.ConfigureAwait(false);
        return await AggregateLoad.FindAsync<TEntity>(context, [key], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Loads the aggregate with <paramref name="key" /> whole — what <c>Product.Find(id)</c> returns, to change
    ///     through its own methods and <c>Save()</c>.
    /// </summary>
    /// <param name="key">The primary key of the row to load.</param>
    /// <param name="cancellationToken">Cancels the load; unset, the work this call belongs to does.</param>
    /// <returns>The aggregate and its children, or <c>null</c> when no row has that key or it is soft-deleted.</returns>
    /// <remarks>
    ///     Always through a context of its own, and handed back DETACHED with what it read remembered beside it: that
    ///     snapshot is what <c>Save()</c> diffs against, so the row is read once however it is saved. A read that only
    ///     shows a row belongs on the read face (<c>Product.Where(…)</c>) or the form model (<c>Product.Model(id)</c>).
    /// </remarks>
    public static async Task<TEntity?> Find<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        object key,
        CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        cancellationToken = Ambient.Or(cancellationToken);
        ArgumentNullException.ThrowIfNull(key);

        // Tracked in a context about to be discarded, only so what was read can be remembered for Save().
        var context = Db.CreateContext();
        await using var contextScope = context.ConfigureAwait(false);
        var found = await AggregateLoad.FindAsync<TEntity>(context, [key], cancellationToken, tracked: true)
            .ConfigureAwait(false);

        if (found is not null)
        {
            AggregateSave.Remember(context, found);
        }

        return found;
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
            // Inside Db.Transaction a handed-in context joins it, or is refused — it never commits on its own beside it.
            AmbientTransaction.Admit(db);
            return await write(db).ConfigureAwait(false);
        }

        var context = Db.CreateContext();
        await using var contextScope = context.ConfigureAwait(false);
        var result = await write(context).ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    // Tracked, and whole: a write that applied a change to a collection which had not been loaded would see an
    // empty one and sync it away. The read path loads by key the same way, so both halves of "load one root"
    // agree about what a root is.
    private static async Task<TEntity> LoadAsync<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(DbContext context, object key, CancellationToken cancellationToken)
        where TEntity : class, IAggregate
    {
        // Before the load, which would only report the row missing: with no tenant resolved every row is.
        Tenant.DemandForWrite(typeof(TEntity));

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

        var entry = context.Entry(entity);

        if (entry.Metadata.FindProperty(Columns.Version) is null)
        {
            throw new InvalidOperationException(
                $"{entry.Metadata.ClrType.Name} does not implement IVersioned, so it has no Version to compare " +
                $"{expected} with. Call the write without a version, or remove the const to keep the check.");
        }

        entry.Property(Columns.Version).OriginalValue = expected;
    }
}
