using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Rask.Data;

/// <summary>
///     Puts <see cref="DbSet{TEntity}" />'s surface on the entity type itself, so
///     <c>Product.Add(…)</c>, <c>Product.Where(…)</c> and <c>Product.FindAsync(…)</c> read the same as
///     <c>db.Products.Add(…)</c> without a <see cref="DbContext" /> in scope to reach them through.
/// </summary>
/// <remarks>
///     <para>
///         These are C# 14 static extension members over every type deriving from <see cref="Model" />,
///         which is why nothing has to be declared, generated, or derived from a second base: an entity
///         that compiles today has them.
///     </para>
///     <para>
///         <b>They are EF Core's own members, with EF Core's own meanings.</b> <c>Add</c>, <c>Remove</c>
///         and <c>Update</c> tell the change tracker what happened; they write nothing until a
///         <c>SaveChanges</c>, exactly as on a <see cref="DbSet{TEntity}" />. So they need an ambient
///         <see cref="UnitOfWork" /> to track against, and say so plainly when there is none rather than
///         appearing to work:
///     </para>
///     <example>
///         <code>
/// await using var uow = Db.Begin();
///
/// Product.Add(new Product("Anvil"));
/// Product.Remove(discontinued);
///
/// await uow.SaveChangesAsync();
///         </code>
///     </example>
///     <para>
///         The query members need no such thing — they return an <see cref="ModelQuery{TEntity}" />
///         that opens a context when it runs and disposes it before it returns, so
///         <c>await Product.Where(p =&gt; p.Active).ToListAsync()</c> is a complete statement anywhere.
///         Rows come back untracked unless <see cref="ModelQuery{TEntity}.AsTracking" /> asks otherwise.
///     </para>
///     <para>
///         A member declared on the entity itself always wins over one of these, so an entity with its
///         own <c>Create</c> or <c>Find</c> keeps it.
///     </para>
/// </remarks>
public static class ModelSet
{
    extension<TEntity>(TEntity)
        where TEntity : Model
    {
        // ---- The set itself -------------------------------------------------------------------

        /// <summary>
        ///     The whole set, as a query — the starting point for anything the operators below do not
        ///     open directly.
        /// </summary>
        /// <example>
        ///     <code>
        /// var deleted = await Product.All
        ///     .IgnoreQueryFilters()
        ///     .Where(p =&gt; p.DeletedAt != null)
        ///     .ToListAsync();
        ///     </code>
        /// </example>
        public static ModelQuery<TEntity> All => new();

        /// <summary>
        ///     The ambient unit of work's <see cref="DbSet{TEntity}" /> — the escape hatch to everything
        ///     EF Core can do that this surface does not wrap.
        /// </summary>
        /// <exception cref="InvalidOperationException">No unit of work is open on this async flow.</exception>
        public static DbSet<TEntity> Set => Db.Current.Set<TEntity>();

        // ---- Tracker verbs: what DbSet<T> does, needing a unit of work to do it in ---------------

        /// <summary>Marks <paramref name="entity" /> for insertion on the next save.</summary>
        /// <exception cref="InvalidOperationException">No unit of work is open on this async flow.</exception>
        public static EntityEntry<TEntity> Add(TEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return Db.Current.Add(entity);
        }

        /// <summary>Marks every entity in <paramref name="entities" /> for insertion on the next save.</summary>
        /// <exception cref="InvalidOperationException">No unit of work is open on this async flow.</exception>
        public static void AddRange(IEnumerable<TEntity> entities)
        {
            ArgumentNullException.ThrowIfNull(entities);
            Db.Current.AddRange(entities);
        }

        /// <summary>Marks every entity in <paramref name="entities" /> for insertion on the next save.</summary>
        /// <exception cref="InvalidOperationException">No unit of work is open on this async flow.</exception>
        public static void AddRange(params TEntity[] entities)
        {
            ArgumentNullException.ThrowIfNull(entities);
            Db.Current.AddRange(entities);
        }

        /// <summary>
        ///     Marks <paramref name="entity" /> as changed, so the next save writes it.
        /// </summary>
        /// <remarks>
        ///     The natural partner of the no-tracking default: read a row, change it, hand it back. It
        ///     attaches a detached entity and marks the whole thing modified, as
        ///     <see cref="DbSet{TEntity}.Update(TEntity)" /> does — every mapped column is written, not
        ///     just the ones you touched.
        /// </remarks>
        /// <exception cref="InvalidOperationException">No unit of work is open on this async flow.</exception>
        public static EntityEntry<TEntity> Update(TEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return Db.Current.Update(entity);
        }

        /// <summary>Marks every entity in <paramref name="entities" /> as changed.</summary>
        /// <exception cref="InvalidOperationException">No unit of work is open on this async flow.</exception>
        public static void UpdateRange(IEnumerable<TEntity> entities)
        {
            ArgumentNullException.ThrowIfNull(entities);
            Db.Current.UpdateRange(entities);
        }

        /// <summary>
        ///     Marks <paramref name="entity" /> for deletion on the next save.
        /// </summary>
        /// <remarks>
        ///     An <see cref="ISoftDeletable" /> is not physically deleted — <see cref="SoftDeleteInterceptor" />
        ///     turns this into a <c>DeletedAt</c> stamp, and the global query filter hides the row from
        ///     then on. That is the point of routing deletes through the tracker rather than through
        ///     <see cref="ModelQuery{TEntity}.ExecuteDeleteAsync" />, which bypasses the interceptors.
        /// </remarks>
        /// <exception cref="InvalidOperationException">No unit of work is open on this async flow.</exception>
        public static EntityEntry<TEntity> Remove(TEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return Db.Current.Remove(entity);
        }

        /// <summary>Marks every entity in <paramref name="entities" /> for deletion on the next save.</summary>
        /// <exception cref="InvalidOperationException">No unit of work is open on this async flow.</exception>
        public static void RemoveRange(IEnumerable<TEntity> entities)
        {
            ArgumentNullException.ThrowIfNull(entities);
            Db.Current.RemoveRange(entities);
        }

        /// <summary>Begins tracking <paramref name="entity" /> as unchanged.</summary>
        /// <exception cref="InvalidOperationException">No unit of work is open on this async flow.</exception>
        public static EntityEntry<TEntity> Attach(TEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return Db.Current.Attach(entity);
        }

        /// <summary>The change-tracking entry for <paramref name="entity" />.</summary>
        /// <exception cref="InvalidOperationException">No unit of work is open on this async flow.</exception>
        public static EntityEntry<TEntity> Entry(TEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return Db.Current.Entry(entity);
        }

        // ---- Query entry points: each opens its own context unless a unit of work is open --------

        /// <summary>Filters the set.</summary>
        public static ModelQuery<TEntity> Where(Expression<Func<TEntity, bool>> predicate) =>
            new ModelQuery<TEntity>().Where(predicate);

        /// <summary>Orders the set ascending.</summary>
        public static ModelQuery<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector) =>
            new ModelQuery<TEntity>().OrderBy(keySelector);

        /// <summary>Orders the set descending.</summary>
        public static ModelQuery<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector) =>
            new ModelQuery<TEntity>().OrderByDescending(keySelector);

        /// <summary>Eager-loads a related navigation.</summary>
        public static ModelQuery<TEntity> Include<TProperty>(Expression<Func<TEntity, TProperty>> navigation) =>
            new ModelQuery<TEntity>().Include(navigation);

        /// <summary>Eager-loads a related navigation named by a path.</summary>
        public static ModelQuery<TEntity> Include(string navigationPropertyPath) =>
            new ModelQuery<TEntity>().Include(navigationPropertyPath);

        /// <summary>Projects the set, reading only the columns the projection names.</summary>
        public static Projection<TEntity, TResult> Select<TResult>(Expression<Func<TEntity, TResult>> selector) =>
            new ModelQuery<TEntity>().Select(selector);

        /// <summary>Takes the first <paramref name="count" /> rows of the unordered set.</summary>
        public static ModelQuery<TEntity> Take(int count) => new ModelQuery<TEntity>().Take(count);

        /// <summary>Skips <paramref name="count" /> rows of the unordered set.</summary>
        public static ModelQuery<TEntity> Skip(int count) => new ModelQuery<TEntity>().Skip(count);

        /// <summary>Returns change-tracked rows rather than the untracked default.</summary>
        public static ModelQuery<TEntity> AsTracking() => new ModelQuery<TEntity>().AsTracking();

        /// <summary>Returns untracked rows, stating the default explicitly.</summary>
        public static ModelQuery<TEntity> AsNoTracking() => new ModelQuery<TEntity>().AsNoTracking();

        /// <summary>
        ///     Drops the model's global query filters, including the one that hides soft-deleted rows.
        /// </summary>
        public static ModelQuery<TEntity> IgnoreQueryFilters() => new ModelQuery<TEntity>().IgnoreQueryFilters();

        /// <summary>Splits the query's joins into separate round trips.</summary>
        public static ModelQuery<TEntity> AsSplitQuery() => new ModelQuery<TEntity>().AsSplitQuery();

        // ---- Terminal reads over the whole set --------------------------------------------------

        /// <summary>
        ///     Finds the row with this key, by primary key.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <see cref="DbSet{TEntity}.FindAsync(object?[])" />'s semantics, including the useful
        ///         one: inside a unit of work that has already loaded this row it is returned from the
        ///         change tracker without a round trip.
        ///     </para>
        ///     <para>
        ///         Unlike the query members, what this returns is <b>tracked</b> when a unit of work is
        ///         open — that is EF Core's behaviour for <c>Find</c>, and it is what makes
        ///         <c>Find</c> then change then save work.
        ///     </para>
        ///     <para>
        ///         The key is typed <see cref="object" /> for the same reason EF Core types it that way:
        ///         an entity's key may be composite, and the key type is not inferable from the entity
        ///         type at a static call site.
        ///     </para>
        /// </remarks>
        public static async Task<TEntity?> FindAsync(object key, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(key);

            await using var unitOfWork = Db.Begin();
            return await unitOfWork.Context.Set<TEntity>()
                .FindAsync([key], cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Finds the row with this composite key.</summary>
        public static async Task<TEntity?> FindAsync(object?[] keyValues, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(keyValues);

            await using var unitOfWork = Db.Begin();
            return await unitOfWork.Context.Set<TEntity>()
                .FindAsync(keyValues, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>Reads the whole set.</summary>
        public static Task<List<TEntity>> ToListAsync(CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().ToListAsync(cancellationToken);

        /// <summary>Reads the whole set as an array.</summary>
        public static Task<TEntity[]> ToArrayAsync(CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().ToArrayAsync(cancellationToken);

        /// <summary>The first row matching <paramref name="predicate" />, or <c>null</c>.</summary>
        public static Task<TEntity?> FirstOrDefaultAsync(
            Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().Where(predicate).FirstOrDefaultAsync(cancellationToken);

        /// <summary>The only row matching <paramref name="predicate" />, or <c>null</c>.</summary>
        /// <exception cref="InvalidOperationException">More than one row matched.</exception>
        public static Task<TEntity?> SingleOrDefaultAsync(
            Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().Where(predicate).SingleOrDefaultAsync(cancellationToken);

        /// <summary>How many rows the set has.</summary>
        public static Task<int> CountAsync(CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().CountAsync(cancellationToken);

        /// <summary>How many rows match <paramref name="predicate" />.</summary>
        public static Task<int> CountAsync(
            Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().Where(predicate).CountAsync(cancellationToken);

        /// <summary>How many rows the set has, as a <see cref="long" />.</summary>
        public static Task<long> LongCountAsync(CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().LongCountAsync(cancellationToken);

        /// <summary>Whether the set has any row at all.</summary>
        public static Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().AnyAsync(cancellationToken);

        /// <summary>Whether any row matches <paramref name="predicate" />.</summary>
        public static Task<bool> AnyAsync(
            Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().Where(predicate).AnyAsync(cancellationToken);

        /// <summary>Streams the whole set, a row at a time.</summary>
        public static IAsyncEnumerable<TEntity> AsAsyncEnumerable(CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().AsAsyncEnumerable(cancellationToken);

        /// <summary>
        ///     Runs <paramref name="query" /> against the set's live <see cref="IQueryable{T}" />, for the
        ///     shapes this surface does not wrap — a group-by, a join, an aggregate.
        /// </summary>
        /// <remarks>
        ///     The context is opened for the call and disposed after it, joining the ambient unit of work
        ///     when there is one. Do not let the <see cref="IQueryable{T}" /> escape the callback.
        /// </remarks>
        public static Task<TResult> QueryAsync<TResult>(
            Func<IQueryable<TEntity>, CancellationToken, Task<TResult>> query,
            CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().QueryAsync(query, cancellationToken);
    }

    extension<TEntity>(TEntity entity)
        where TEntity : Model
    {
        /// <summary>
        ///     Writes this model, so a method on it can finish the job: <c>order.Cancel()</c> changes the
        ///     order, <c>await order.SaveAsync()</c> makes it true.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Outside a unit of work this is one: a context is opened, the model is written, the
        ///         context is disposed. So a self-contained operation needs no <c>Db.Begin()</c> around it:
        ///     </para>
        ///     <example>
        ///         <code>
        /// public async Task CancelAsync(DateTime when, CancellationToken ct = default)
        /// {
        ///     Cancel(when);            // the decision, and nothing else
        ///     await SaveAsync(ct);     // now make it true
        /// }
        ///         </code>
        ///     </example>
        ///     <para>
        ///         <b>Inside a unit of work it joins rather than commits.</b> The change is tracked against
        ///         the open unit of work and written when <em>that</em> commits — so a caller who wrapped
        ///         several models in one transaction still gets one transaction, and a model's own method
        ///         cannot commit half of its caller's work behind its back. It is the same rule
        ///         <see cref="Db.Begin" /> follows for nesting, and it is what makes a method like the one
        ///         above safe to call from anywhere.
        ///     </para>
        ///     <para>
        ///         A model that is not being tracked is attached and marked modified, as
        ///         <see cref="DbSet{TEntity}.Update(TEntity)" /> does — every mapped column is written, not
        ///         only the ones that changed. One already tracked keeps its own change detection, so a
        ///         model read with <c>AsTracking()</c> writes only what it touched.
        ///     </para>
        ///     <para>
        ///         This writes an existing row. A new model is inserted with <c>Add</c>, which says so at
        ///         the call site rather than leaving insert-or-update to be inferred from whether a key
        ///         happens to be set.
        ///     </para>
        /// </remarks>
        public async Task SaveAsync(CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entity);

            await using var unitOfWork = Db.Begin();
            var context = unitOfWork.Context;

            // Update() on an already-tracked model would mark every property modified and throw away the
            // change tracking that makes a targeted UPDATE possible, so only a detached one is attached.
            if (context.Entry(entity).State == EntityState.Detached)
            {
                context.Update(entity);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
