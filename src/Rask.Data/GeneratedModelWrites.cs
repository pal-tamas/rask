using System.ComponentModel;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     The persistence half of the writes generated for every model — <c>Product.CreateAsync(ProductModel)</c>,
///     <c>Product.UpdateAsync(id, ProductModel)</c> and <c>Product.DeleteAsync(id)</c>.
/// </summary>
/// <remarks>
///     <para>
///         Called by generated code, which lives in the application's assembly and so cannot reach an
///         <c>internal</c> member. Not an API to write against: call the generated members.
///     </para>
///     <para>
///         Each write opens a context, makes one change through the change tracker — so the auditing,
///         soft-delete and domain-event interceptors all see it — saves, and disposes the context.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedModelWrites
{
    /// <summary>Inserts <paramref name="entity" />.</summary>
    /// <returns>The inserted entity, with any store-generated key filled in.</returns>
    public static async Task<TEntity> CreateAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
        where TEntity : Model
    {
        ArgumentNullException.ThrowIfNull(entity);

        await using var context = Db.CreateContext();
        context.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
    /// <param name="apply">Copies the caller's values onto the loaded entity.</param>
    /// <param name="cancellationToken">Cancels the load and the save.</param>
    /// <returns>The updated entity.</returns>
    /// <exception cref="KeyNotFoundException">No row has <paramref name="key" /> (or it is soft-deleted).</exception>
    /// <exception cref="DbUpdateConcurrencyException">The row's version is no longer <paramref name="version" />.</exception>
    public static async Task<TEntity> UpdateAsync<TEntity>(
        object key,
        int? version,
        Action<TEntity> apply,
        CancellationToken cancellationToken = default)
        where TEntity : Model
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(apply);

        await using var context = Db.CreateContext();
        var entity = await LoadAsync<TEntity>(context, key, cancellationToken).ConfigureAwait(false);

        ExpectVersion(context, entity, version);
        apply(entity);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entity;
    }

    /// <summary>
    ///     Deletes the row with <paramref name="key" /> through the change tracker, so an
    ///     <see cref="ISoftDeletable" /> is stamped rather than removed.
    /// </summary>
    /// <param name="key">The primary key of the row to delete.</param>
    /// <param name="version">
    ///     The <see cref="Columns.Version" /> the caller last saw, or <c>null</c> to delete whatever the
    ///     current version is.
    /// </param>
    /// <param name="cancellationToken">Cancels the load and the save.</param>
    /// <exception cref="KeyNotFoundException">No row has <paramref name="key" /> (or it is soft-deleted).</exception>
    /// <exception cref="DbUpdateConcurrencyException">The row's version is no longer <paramref name="version" />.</exception>
    public static async Task DeleteAsync<TEntity>(object key, int? version, CancellationToken cancellationToken = default)
        where TEntity : Model
    {
        ArgumentNullException.ThrowIfNull(key);

        await using var context = Db.CreateContext();
        var entity = await LoadAsync<TEntity>(context, key, cancellationToken).ConfigureAwait(false);

        ExpectVersion(context, entity, version);
        context.Remove(entity);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TEntity> LoadAsync<TEntity>(DbContext context, object key, CancellationToken cancellationToken)
        where TEntity : Model =>
        await context.Set<TEntity>().FindAsync([key], cancellationToken).ConfigureAwait(false)
        ?? throw new KeyNotFoundException(
            $"There is no {typeof(TEntity).Name} with key '{key}' — it was never created, or it has been " +
            "deleted since it was read.");

    // The save's WHERE clause compares against the ORIGINAL value of a concurrency token, and a freshly
    // loaded row's original is whatever the database holds now — which would make every check pass. Pinning
    // the original to the version the caller read is what turns "someone saved since" into an exception.
    private static void ExpectVersion(DbContext context, Model entity, int? version)
    {
        if (version is not { } expected)
        {
            return;
        }

        var entry = context.Entry(entity);
        if (entry.Metadata.FindProperty(Columns.Version) is null)
        {
            throw new InvalidOperationException(
                $"'{entity.GetType().Name}' has no {Columns.Version} to check a version against; implement " +
                $"{nameof(IVersioned)} to guard it with optimistic concurrency.");
        }

        entry.Property(Columns.Version).OriginalValue = expected;
    }
}
