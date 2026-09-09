using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Rask.Data;

/// <summary>
///     A query against <typeparamref name="TEntity" /> that has not run yet, and does not hold a
///     <see cref="DbContext" /> open while you build it.
/// </summary>
/// <remarks>
///     <para>
///         This is what <c>Product.Where(…)</c>, <c>Product.All</c> and friends return. It composes
///         LINQ operators the way <see cref="IQueryable{T}" /> does, but records them instead of
///         binding to a context — the context is opened by the terminal call
///         (<c>ToListAsync</c>, <c>FirstOrDefaultAsync</c>, <c>CountAsync</c>, …)
///         and disposed before it returns.
///     </para>
///     <para>
///         That is what makes a read need no ceremony: <c>await Product.Where(p =&gt; p.Active).ToListAsync()</c>
///         is complete and leaks nothing. Inside an open <see cref="UnitOfWork" /> the terminal call joins
///         that unit's context instead of opening one of its own.
///     </para>
///     <para>
///         Rows come back <b>untracked</b> unless <see cref="AsTracking" /> asks otherwise — see that
///         method for why, and for what it means when you want to write one back.
///     </para>
///     <para>
///         Every operator returns a new instance, so a partially-built query is safe to hold in a field
///         and branch from.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The entity being queried.</typeparam>
public sealed class ModelQuery<TEntity>
    where TEntity : Model
{
    private readonly Func<IQueryable<TEntity>, IQueryable<TEntity>>? _compose;
    private readonly bool _ordered;
    private readonly bool _tracking;

    internal ModelQuery()
    {
    }

    private ModelQuery(Func<IQueryable<TEntity>, IQueryable<TEntity>>? compose, bool ordered, bool tracking)
    {
        _compose = compose;
        _ordered = ordered;
        _tracking = tracking;
    }

    /// <summary>Filters the query. Composes with any filter already applied.</summary>
    public ModelQuery<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Then(q => q.Where(predicate), _ordered);
    }

    /// <summary>Orders the query ascending, replacing any ordering already applied.</summary>
    public ModelQuery<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return Then(q => q.OrderBy(keySelector), ordered: true);
    }

    /// <summary>Orders the query descending, replacing any ordering already applied.</summary>
    public ModelQuery<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return Then(q => q.OrderByDescending(keySelector), ordered: true);
    }

    /// <summary>Adds a secondary ascending ordering.</summary>
    /// <exception cref="InvalidOperationException">Nothing has been ordered yet.</exception>
    public ModelQuery<TEntity> ThenBy<TKey>(Expression<Func<TEntity, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        RequireOrdering(nameof(ThenBy));
        return Then(q => ((IOrderedQueryable<TEntity>)q).ThenBy(keySelector), ordered: true);
    }

    /// <summary>Adds a secondary descending ordering.</summary>
    /// <exception cref="InvalidOperationException">Nothing has been ordered yet.</exception>
    public ModelQuery<TEntity> ThenByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        RequireOrdering(nameof(ThenByDescending));
        return Then(q => ((IOrderedQueryable<TEntity>)q).ThenByDescending(keySelector), ordered: true);
    }

    /// <summary>Skips <paramref name="count" /> rows.</summary>
    public ModelQuery<TEntity> Skip(int count) => Then(q => q.Skip(count), _ordered);

    /// <summary>Takes at most <paramref name="count" /> rows.</summary>
    public ModelQuery<TEntity> Take(int count) => Then(q => q.Take(count), _ordered);

    /// <summary>Eager-loads a related navigation.</summary>
    public ModelQuery<TEntity> Include<TProperty>(Expression<Func<TEntity, TProperty>> navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        return Then(q => q.Include(navigation), _ordered);
    }

    /// <summary>Eager-loads a related navigation named by a path, as EF Core's string overload does.</summary>
    public ModelQuery<TEntity> Include(string navigationPropertyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(navigationPropertyPath);
        return Then(q => q.Include(navigationPropertyPath), _ordered);
    }

    /// <summary>Returns entities that are not change-tracked. <b>The default</b>, stated explicitly.</summary>
    public ModelQuery<TEntity> AsNoTracking() => With(_compose, _ordered, tracking: false);

    /// <summary>
    ///     Returns change-tracked entities, so that changing one and calling
    ///     <see cref="UnitOfWork.SaveChangesAsync" /> writes it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Queries are <b>no-tracking by default</b>, which is the right default for a UI: most reads
    ///         are rendered and never written back, and outside a unit of work the context is discarded as
    ///         the call returns, so tracking would cost a graph walk and an identity-map entry to buy
    ///         nothing.
    ///     </para>
    ///     <para>
    ///         The consequence to know: mutating an entity that came back untracked and then saving does
    ///         <em>nothing</em>. Write it back explicitly with <c>Product.Update(entity)</c> — which is
    ///         the ordinary way round here — or read it with this and mutate it in place.
    ///     </para>
    /// </remarks>
    public ModelQuery<TEntity> AsTracking() => With(_compose, _ordered, tracking: true);

    /// <summary>
    ///     Drops the model's global query filters — notably the one
    ///     <see cref="ModelBuilderExtensions.ApplyRaskConventions" /> adds to every
    ///     <see cref="ISoftDeletable" />, so this is how soft-deleted rows are listed or restored.
    /// </summary>
    public ModelQuery<TEntity> IgnoreQueryFilters() => Then(q => q.IgnoreQueryFilters(), _ordered);

    /// <summary>Splits the query's joins into separate round trips.</summary>
    public ModelQuery<TEntity> AsSplitQuery() => Then(q => q.AsSplitQuery(), _ordered);

    /// <summary>Projects each row, giving a query that returns <typeparamref name="TResult" />.</summary>
    /// <remarks>
    ///     The point of projecting in the database rather than after <see cref="ToListAsync" /> is that
    ///     the unread columns are never fetched. The result is no longer an entity, so it has the read
    ///     operators and none of the tracker ones.
    /// </remarks>
    public Projection<TEntity, TResult> Select<TResult>(Expression<Func<TEntity, TResult>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new Projection<TEntity, TResult>(Apply, selector);
    }

    /// <summary>Runs the query and returns every row.</summary>
    public Task<List<TEntity>> ToListAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.ToListAsync(ct), cancellationToken);

    /// <summary>Runs the query and returns every row as an array.</summary>
    public Task<TEntity[]> ToArrayAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.ToArrayAsync(ct), cancellationToken);

    /// <summary>Runs the query and returns its first row, or <c>null</c> when it matched nothing.</summary>
    public Task<TEntity?> FirstOrDefaultAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.FirstOrDefaultAsync(ct), cancellationToken);

    /// <summary>Filters, then returns the first match or <c>null</c>.</summary>
    public Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Where(predicate).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    ///     Runs the query and returns its only row, or <c>null</c> when it matched nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">The query matched more than one row.</exception>
    public Task<TEntity?> SingleOrDefaultAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.SingleOrDefaultAsync(ct), cancellationToken);

    /// <summary>Counts the matching rows.</summary>
    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.CountAsync(ct), cancellationToken);

    /// <summary>Counts the matching rows as a <see cref="long" />.</summary>
    public Task<long> LongCountAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.LongCountAsync(ct), cancellationToken);

    /// <summary>Whether the query matches any row.</summary>
    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.AnyAsync(ct), cancellationToken);

    /// <summary>
    ///     Deletes every matching row in one statement, without loading or tracking them.
    /// </summary>
    /// <remarks>
    ///     <b>This bypasses the interceptors.</b> It is a <c>DELETE … WHERE</c> issued by the database, so
    ///     nothing raises a domain event, stamps <c>UpdatedAt</c>, or turns the delete into a soft delete
    ///     — an <see cref="ISoftDeletable" /> deleted this way is really gone. That is the trade for not
    ///     round-tripping the rows; when you want the conventions, load and remove.
    /// </remarks>
    public Task<int> ExecuteDeleteAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.ExecuteDeleteAsync(ct), cancellationToken);

    /// <summary>
    ///     Updates every matching row in one statement, without loading or tracking them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One <c>UPDATE … WHERE</c>, so a million rows cost one round trip rather than a million
    ///         entities in memory. The setters may read the row they are updating, which is what makes a
    ///         counter or a relative adjustment possible in the database:
    ///     </para>
    ///     <example>
    ///         <code>
    /// await Product.Where(p =&gt; p.Discontinued)
    ///     .ExecuteUpdateAsync(s =&gt; s
    ///         .SetProperty(p =&gt; p.Active, false)
    ///         .SetProperty(p =&gt; p.Price, p =&gt; p.Price * 0.9m));
    ///         </code>
    ///     </example>
    ///     <para>
    ///         <b>This bypasses the interceptors</b>, exactly as EF Core's own
    ///         <c>ExecuteUpdateAsync</c> does, and the
    ///         omissions are the conventions Rask.Data otherwise maintains for you: no <c>UpdatedAt</c>
    ///         stamp, no <c>Version</c> bump, and no domain events — nothing was loaded to raise any. Set
    ///         them yourself when they matter:
    ///     </para>
    ///     <example>
    ///         <code>
    /// await Product.Where(p =&gt; p.Discontinued)
    ///     .ExecuteUpdateAsync(s =&gt; s
    ///         .SetProperty(p =&gt; p.Active, false)
    ///         .SetProperty(p =&gt; p.UpdatedAt, DateTime.UtcNow)
    ///         .SetProperty(p =&gt; p.Version, p =&gt; p.Version + 1));
    ///         </code>
    ///     </example>
    ///     <para>
    ///         A batch <em>soft</em> delete is this, not <see cref="ExecuteDeleteAsync" /> — which really
    ///         deletes, an <see cref="ISoftDeletable" /> included:
    ///     </para>
    ///     <example>
    ///         <code>
    /// await Product.Where(p =&gt; p.Discontinued)
    ///     .ExecuteUpdateAsync(s =&gt; s.SetProperty(p =&gt; p.DeletedAt, DateTime.UtcNow));
    ///         </code>
    ///     </example>
    ///     <para>
    ///         The rule of thumb: reach for this when the work is a statement the database can do on its
    ///         own, and load-then-save when the conventions and the domain events are the point.
    ///     </para>
    /// </remarks>
    /// <param name="setters">The columns to write, chained with <c>SetProperty</c>.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>The number of rows updated.</returns>
    public Task<int> ExecuteUpdateAsync(
        Action<UpdateSettersBuilder<TEntity>> setters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setters);
        return RunAsync((q, ct) => q.ExecuteUpdateAsync(setters, ct), cancellationToken);
    }

    /// <summary>Enumerates the query, streaming rows as the database produces them.</summary>
    /// <remarks>
    ///     The context stays open for the lifetime of the enumeration, so consume it promptly — this is
    ///     for a large read that should not be materialised whole, not for holding a cursor across
    ///     renders.
    /// </remarks>
    public async IAsyncEnumerable<TEntity> AsAsyncEnumerable(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = Db.Begin();

        await foreach (var entity in Apply(unitOfWork.Context.Set<TEntity>())
                           .AsAsyncEnumerable()
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return entity;
        }
    }

    /// <summary>
    ///     Runs <paramref name="query" /> against the live <see cref="IQueryable{T}" />, for the shapes
    ///     this type does not wrap — a group-by, a join, an aggregate.
    /// </summary>
    /// <remarks>
    ///     The escape hatch, and it is a real one: the operators composed so far are applied first, the
    ///     context is opened for the call and disposed after it, and inside a unit of work it joins that
    ///     one. Do not let the <see cref="IQueryable{T}" /> escape the callback — it dies with the
    ///     context.
    /// </remarks>
    public async Task<TResult> QueryAsync<TResult>(
        Func<IQueryable<TEntity>, CancellationToken, Task<TResult>> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var unitOfWork = Db.Begin();
        return await query(Apply(unitOfWork.Context.Set<TEntity>()), cancellationToken).ConfigureAwait(false);
    }

    // Tracking is decided at the head of the query rather than composed, so that AsTracking() and
    // AsNoTracking() are last-one-wins wherever they appear in the chain instead of order-dependent.
    internal IQueryable<TEntity> Apply(IQueryable<TEntity> source)
    {
        var head = _tracking ? source.AsTracking() : source.AsNoTracking();
        return _compose is null ? head : _compose(head);
    }

    private ModelQuery<TEntity> Then(Func<IQueryable<TEntity>, IQueryable<TEntity>> step, bool ordered)
    {
        var previous = _compose;
        return With(previous is null ? step : q => step(previous(q)), ordered, _tracking);
    }

    private ModelQuery<TEntity> With(
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? compose,
        bool ordered,
        bool tracking) => new(compose, ordered, tracking);

    private void RequireOrdering(string member)
    {
        if (!_ordered)
        {
            throw new InvalidOperationException(
                $"{member} needs an ordering to add to — call OrderBy or OrderByDescending first.");
        }
    }

    private async Task<TResult> RunAsync<TResult>(
        Func<IQueryable<TEntity>, CancellationToken, Task<TResult>> run,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = Db.Begin();
        return await run(Apply(unitOfWork.Context.Set<TEntity>()), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
///     A query that projects <typeparamref name="TEntity" /> rows to <typeparamref name="TResult" />,
///     produced by <see cref="ModelQuery{TEntity}.Select{TResult}" />.
/// </summary>
/// <remarks>
///     Read-only by construction: a projection is not an entity, so there is nothing to track, save or
///     delete through it.
/// </remarks>
/// <typeparam name="TEntity">The entity being read.</typeparam>
/// <typeparam name="TResult">What each row is projected to.</typeparam>
public sealed class Projection<TEntity, TResult>
    where TEntity : Model
{
    private readonly Func<IQueryable<TEntity>, IQueryable<TEntity>> _source;
    private readonly Expression<Func<TEntity, TResult>> _selector;

    internal Projection(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> source,
        Expression<Func<TEntity, TResult>> selector)
    {
        _source = source;
        _selector = selector;
    }

    /// <summary>Runs the query and returns every projected row.</summary>
    public Task<List<TResult>> ToListAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.ToListAsync(ct), cancellationToken);

    /// <summary>Runs the query and returns every projected row as an array.</summary>
    public Task<TResult[]> ToArrayAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.ToArrayAsync(ct), cancellationToken);

    /// <summary>Runs the query and returns the first projected row, or the default when empty.</summary>
    public Task<TResult?> FirstOrDefaultAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.FirstOrDefaultAsync(ct), cancellationToken)!;

    /// <summary>Counts the matching rows.</summary>
    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.CountAsync(ct), cancellationToken);

    /// <summary>Whether the query matches any row.</summary>
    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.AnyAsync(ct), cancellationToken);

    private async Task<TValue> RunAsync<TValue>(
        Func<IQueryable<TResult>, CancellationToken, Task<TValue>> run,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = Db.Begin();
        var projected = _source(unitOfWork.Context.Set<TEntity>()).Select(_selector);
        return await run(projected, cancellationToken).ConfigureAwait(false);
    }
}
