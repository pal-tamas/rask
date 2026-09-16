using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     A query against <typeparamref name="TEntity" /> that has not run yet, and does not hold a
///     <see cref="DbContext" /> open while you build it.
/// </summary>
/// <remarks>
///     <para>
///         This is what <c>Product.Where(…)</c>, <c>Product.All</c> and friends return. It composes LINQ
///         operators the way <see cref="IQueryable{T}" /> does, but records them instead of binding to a
///         context — the context is opened by the terminal call (<c>ToListAsync</c>,
///         <c>FirstOrDefaultAsync</c>, <c>CountAsync</c>, …) and disposed before it returns.
///     </para>
///     <para>
///         That is what makes a read need no ceremony: <c>await Product.Where(p =&gt; p.Active).ToListAsync()</c>
///         is complete and leaks nothing.
///     </para>
///     <para>
///         <b>Rows always come back untracked.</b> Most reads are rendered and never written back, and the
///         context is discarded as the call returns, so tracking would cost a graph walk and an identity-map
///         entry to buy nothing. Nothing here writes: a change is a domain method saved through an injected
///         context, which loads the entity it is about to change.
///     </para>
///     <para>
///         Every operator returns a new instance, so a partially-built query is safe to hold in a field and
///         branch from.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The entity being queried.</typeparam>
public sealed class ModelQuery<TEntity>
    where TEntity : class
{
    private readonly Func<IQueryable<TEntity>, IQueryable<TEntity>>? _compose;
    private readonly bool _ordered;

    // A Search whose text held no word filters and ranks nothing, but the caller wrote it as an ordering, so a ThenBy
    // after it has to keep working — as the ordering itself — rather than fail exactly while the search box is empty.
    private readonly bool _unrankedSearch;

    internal ModelQuery()
    {
    }

    private ModelQuery(Func<IQueryable<TEntity>, IQueryable<TEntity>>? compose, bool ordered, bool unrankedSearch)
    {
        _compose = compose;
        _ordered = ordered;
        _unrankedSearch = unrankedSearch;
    }

    /// <summary>Filters the query. Composes with any filter already applied.</summary>
    public ModelQuery<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Then(q => q.Where(predicate), _ordered, _unrankedSearch);
    }

    /// <summary>
    ///     Narrows the query to the rows whose indexed text contains every word of <paramref name="text" />, best
    ///     match first. The entity must declare <c>HasFullTextSearch</c>.
    /// </summary>
    /// <remarks>
    ///     Text with no word in it filters nothing, so an empty search box lists everything. A later
    ///     <see cref="OrderBy{TKey}" /> replaces best-match order. See
    ///     <see cref="FullTextQueryableExtensions.Search{TEntity}" />.
    /// </remarks>
    public ModelQuery<TEntity> Search(string? text) =>
        FullTextQuery.Compile(text) is null
            ? _ordered ? this : new ModelQuery<TEntity>(_compose, _ordered, unrankedSearch: true)
            : Then(q => q.Search(text), ordered: true);

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
        if (_unrankedSearch)
        {
            return OrderBy(keySelector);
        }

        RequireOrdering(nameof(ThenBy));
        return Then(q => ((IOrderedQueryable<TEntity>)q).ThenBy(keySelector), ordered: true);
    }

    /// <summary>Adds a secondary descending ordering.</summary>
    /// <exception cref="InvalidOperationException">Nothing has been ordered yet.</exception>
    public ModelQuery<TEntity> ThenByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        if (_unrankedSearch)
        {
            return OrderByDescending(keySelector);
        }

        RequireOrdering(nameof(ThenByDescending));
        return Then(q => ((IOrderedQueryable<TEntity>)q).ThenByDescending(keySelector), ordered: true);
    }

    /// <summary>Skips <paramref name="count" /> rows.</summary>
    public ModelQuery<TEntity> Skip(int count) => Then(q => q.Skip(count), _ordered, _unrankedSearch);

    /// <summary>Takes at most <paramref name="count" /> rows.</summary>
    public ModelQuery<TEntity> Take(int count) => Then(q => q.Take(count), _ordered, _unrankedSearch);

    /// <summary>Eager-loads a related navigation.</summary>
    public ModelQuery<TEntity> Include<TProperty>(Expression<Func<TEntity, TProperty>> navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        return Then(q => q.Include(navigation), _ordered, _unrankedSearch);
    }

    /// <summary>Eager-loads a related navigation named by a path, as EF Core's string overload does.</summary>
    public ModelQuery<TEntity> Include(string navigationPropertyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(navigationPropertyPath);
        return Then(q => q.Include(navigationPropertyPath), _ordered, _unrankedSearch);
    }

    /// <summary>
    ///     Drops the model's global query filters — notably the one
    ///     <see cref="ModelBuilderExtensions.ApplyRaskConventions" /> adds to every
    ///     <see cref="Aggregate{TId}" />, so this is how soft-deleted rows are listed or restored.
    /// </summary>
    public ModelQuery<TEntity> IgnoreQueryFilters() => Then(q => q.IgnoreQueryFilters(), _ordered, _unrankedSearch);

    /// <summary>Splits the query's joins into separate round trips.</summary>
    public ModelQuery<TEntity> AsSplitQuery() => Then(q => q.AsSplitQuery(), _ordered, _unrankedSearch);

    /// <summary>Projects each row, giving a query that returns <typeparamref name="TResult" />.</summary>
    /// <remarks>
    ///     The point of projecting in the database rather than after <see cref="ToListAsync" /> is that the
    ///     unread columns are never fetched. The result is no longer an entity, so it has the read operators
    ///     and nothing else.
    /// </remarks>
    public Projection<TEntity, TResult> Select<TResult>(Expression<Func<TEntity, TResult>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new Projection<TEntity, TResult>(Apply, selector);
    }

    /// <summary>
    ///     This query as a standard <see cref="IQueryable{T}" /> that holds no context, for a component that
    ///     composes its own LINQ — <c>UiDataGrid.Data(Product.Where(p =&gt; p.Active).AsQueryable())</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every execution — a synchronous <c>Count()</c> or <c>ToList()</c>, an awaited
    ///         <c>ToListAsync()</c> or <c>CountAsync()</c> — opens a context for itself and disposes it
    ///         afterwards, so the queryable is safe to hold in a field for as long as a page lives.
    ///         Rows are untracked, as they are everywhere else.
    ///     </para>
    ///     <para>
    ///         The operators composed on this query so far travel with it, EF Core's own included. EF Core's
    ///         extension methods called on the queryable this returns do nothing, because EF only applies
    ///         them to its own provider — put <c>Include</c> or <c>IgnoreQueryFilters</c> here first.
    ///     </para>
    /// </remarks>
    public IQueryable<TEntity> AsQueryable() => new ModelQueryProvider<TEntity>(this).Root;

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

    /// <summary>Enumerates the query, streaming rows as the database produces them.</summary>
    /// <remarks>
    ///     The context stays open for the lifetime of the enumeration, so consume it promptly — this is for
    ///     a large read that should not be materialised whole, not for holding a cursor across renders.
    /// </remarks>
    public async IAsyncEnumerable<TEntity> AsAsyncEnumerable(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var context = ReadDb.OpenFor<TEntity>();

        await foreach (var entity in Apply(context.Set<TEntity>())
                           .AsAsyncEnumerable()
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return entity;
        }
    }

    /// <summary>
    ///     Runs <paramref name="query" /> against the live <see cref="IQueryable{T}" />, for the shapes this
    ///     type does not wrap — a group-by, a join, an aggregate.
    /// </summary>
    /// <remarks>
    ///     The escape hatch, and it is a real one: the operators composed so far are applied first, and the
    ///     context is opened for the call and disposed after it. Do not let the <see cref="IQueryable{T}" />
    ///     escape the callback — it dies with the context; <see cref="AsQueryable" /> is the one that
    ///     outlives a call.
    /// </remarks>
    public async Task<TResult> QueryAsync<TResult>(
        Func<IQueryable<TEntity>, CancellationToken, Task<TResult>> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var context = ReadDb.OpenFor<TEntity>();
        return await query(Apply(context.Set<TEntity>()), cancellationToken).ConfigureAwait(false);
    }

    // Untracked is decided at the head of the query rather than composed, so every read — through this
    // type, a projection, or AsQueryable — starts from the same no-tracking source.
    internal IQueryable<TEntity> Apply(IQueryable<TEntity> source)
    {
        var head = source.AsNoTracking();
        return _compose is null ? head : _compose(head);
    }

    private ModelQuery<TEntity> Then(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> step,
        bool ordered,
        bool unrankedSearch = false)
    {
        var previous = _compose;
        return new ModelQuery<TEntity>(previous is null ? step : q => step(previous(q)), ordered, unrankedSearch);
    }

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
        await using var context = ReadDb.OpenFor<TEntity>();
        return await run(Apply(context.Set<TEntity>()), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
///     A query that projects <typeparamref name="TEntity" /> rows to <typeparamref name="TResult" />,
///     produced by <see cref="ModelQuery{TEntity}.Select{TResult}" />.
/// </summary>
/// <remarks>
///     Read-only by construction: a projection is not an entity, so there is nothing to save or delete
///     through it.
/// </remarks>
/// <typeparam name="TEntity">The entity being read.</typeparam>
/// <typeparam name="TResult">What each row is projected to.</typeparam>
public sealed class Projection<TEntity, TResult>
    where TEntity : class
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
        await using var context = ReadDb.OpenFor<TEntity>();
        var projected = _source(context.Set<TEntity>()).Select(_selector);
        return await run(projected, cancellationToken).ConfigureAwait(false);
    }
}
