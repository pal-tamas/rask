using System.Diagnostics.CodeAnalysis;
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
///         context — the context is opened when the query is awaited (or by <c>First</c>, <c>Count</c>, …)
///         and disposed before it returns.
///     </para>
///     <para>
///         That is what makes a read need no ceremony: <c>await Product.Where(p =&gt; p.Active)</c>
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
public sealed class ModelQuery<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>
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
    public ModelQuery<TEntity> Search(string? text)
    {
        if (FullTextQuery.Compile(text) is not null)
        {
            return Then(q => q.Search(text), ordered: true);
        }

        return _ordered ? this : new ModelQuery<TEntity>(_compose, _ordered, unrankedSearch: true);
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
    ///     Includes soft-deleted rows: drops the filter
    ///     <see cref="ModelBuilderExtensions.ApplyRaskConventions(Microsoft.EntityFrameworkCore.ModelBuilder)" /> adds to every
    ///     <see cref="Aggregate{TId}" />, so this is how soft-deleted rows are listed or restored.
    /// </summary>
    /// <remarks>
    ///     <b>It does not cross tenants.</b> The tenant filter is a separate, named filter and stays exactly
    ///     where it is, so an existing call to this never quietly becomes a cross-tenant read the day an
    ///     aggregate declares <see cref="Tenancy.PerTenant" />. Spanning tenants is
    ///     <see cref="Tenant.Across" />, deliberately and visibly.
    /// </remarks>
    public ModelQuery<TEntity> IgnoreQueryFilters() =>
        Then(q => q.IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter]), _ordered, _unrankedSearch);

    /// <summary>Splits the query's joins into separate round trips.</summary>
    public ModelQuery<TEntity> AsSplitQuery() => Then(q => q.AsSplitQuery(), _ordered, _unrankedSearch);

    /// <summary>Projects each row, giving a query that returns <typeparamref name="TResult" />.</summary>
    /// <remarks>
    ///     The point of projecting in the database rather than after awaiting the query is that the
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
    ///     composes its own LINQ — <c>Ui.DataGrid.Data(Product.Where(p =&gt; p.Active).AsQueryable())</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every execution — a synchronous <c>Count()</c> or <c>ToList()</c>, an awaited
    ///         EF Core <c>ToListAsync()</c> or <c>CountAsync()</c> — opens a context for itself and disposes it
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

    /// <summary>Runs the query and hands back every row: <c>await Product.Where(p =&gt; p.InStock)</c>.</summary>
    /// <remarks>Cancelled by <see cref="Current.Cancellation" />, the work this read belongs to.</remarks>
    public System.Runtime.CompilerServices.TaskAwaiter<List<TEntity>> GetAwaiter() =>
        RunAsync(static (q, ct) => q.ToListAsync(ct), default).GetAwaiter();

    /// <summary>Runs the query as <see cref="GetAwaiter" /> does, resuming on the captured context or not.</summary>
    /// <param name="continueOnCapturedContext">Whether to resume on the context the await started on.</param>
    public System.Runtime.CompilerServices.ConfiguredTaskAwaitable<List<TEntity>> ConfigureAwait(bool continueOnCapturedContext) =>
        RunAsync(static (q, ct) => q.ToListAsync(ct), default).ConfigureAwait(continueOnCapturedContext);

    /// <summary>The first row, or <c>null</c> when the query matched nothing.</summary>
    public Task<TEntity?> First(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.FirstOrDefaultAsync(ct), cancellationToken);

    /// <summary>The first row matching <paramref name="predicate" />, or <c>null</c>.</summary>
    public Task<TEntity?> First(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Where(predicate).First(cancellationToken);
    }

#pragma warning disable CA1720 // Single is LINQ's word for "the only row", which a .NET reader already knows
    /// <summary>The only row, or <c>null</c> when the query matched nothing.</summary>
    /// <exception cref="InvalidOperationException">The query matched more than one row.</exception>
    public Task<TEntity?> Single(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.SingleOrDefaultAsync(ct), cancellationToken);

    /// <summary>The only row matching <paramref name="predicate" />, or <c>null</c>.</summary>
    /// <exception cref="InvalidOperationException">More than one row matched.</exception>
    public Task<TEntity?> Single(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Where(predicate).Single(cancellationToken);
    }
#pragma warning restore CA1720

    /// <summary>How many rows match.</summary>
    public Task<int> Count(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.CountAsync(ct), cancellationToken);

    /// <summary>How many rows match <paramref name="predicate" />.</summary>
    public Task<int> Count(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Where(predicate).Count(cancellationToken);
    }

    /// <summary>How many rows match, as a <see cref="long" />.</summary>
    public Task<long> LongCount(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.LongCountAsync(ct), cancellationToken);

    /// <summary>Whether the query matches any row.</summary>
    public Task<bool> Any(CancellationToken cancellationToken = default) =>
        RunAsync(static (q, ct) => q.AnyAsync(ct), cancellationToken);

    /// <summary>Whether any row matches <paramref name="predicate" />.</summary>
    public Task<bool> Any(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Where(predicate).Any(cancellationToken);
    }

    /// <summary>Enumerates the query, streaming rows as the database produces them.</summary>
    /// <remarks>
    ///     The context stays open for the lifetime of the enumeration, so consume it promptly — this is for
    ///     a large read that should not be materialised whole, not for holding a cursor across renders.
    /// </remarks>
    public async IAsyncEnumerable<TEntity> AsAsyncEnumerable(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var context = ReadDb.OpenFor<TEntity>();
        await using var contextScope = context.ConfigureAwait(false);

        await foreach (var entity in Apply(context.Set<TEntity>())
                           .AsAsyncEnumerable()
                           .WithCancellation(Ambient.Or(cancellationToken))
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
    public async Task<TResult> Query<TResult>(
        Func<IQueryable<TEntity>, CancellationToken, Task<TResult>> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var context = ReadDb.OpenFor<TEntity>();
        await using var contextScope = context.ConfigureAwait(false);
        return await query(Apply(context.Set<TEntity>()), Ambient.Or(cancellationToken)).ConfigureAwait(false);
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
        var context = ReadDb.OpenFor<TEntity>();
        await using var contextScope = context.ConfigureAwait(false);
        return await run(Apply(context.Set<TEntity>()), Ambient.Or(cancellationToken)).ConfigureAwait(false);
    }
}
