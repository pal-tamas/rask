using System.Collections;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Rask.Data;

/// <summary>
///     What <see cref="ModelQuery{TEntity}.AsQueryable" /> hands back: an <see cref="IQueryable{T}" /> that
///     holds no context, and opens one each time it is executed.
/// </summary>
/// <remarks>
///     Implements <see cref="IAsyncEnumerable{T}" /> so EF Core's <c>ToListAsync</c> and friends accept it,
///     and <see cref="IOrderedQueryable{T}" /> so <c>OrderBy(…).ThenBy(…)</c> composes on it.
/// </remarks>
internal sealed class ModelQueryable<TElement> : IOrderedQueryable<TElement>, IAsyncEnumerable<TElement>
{
    private readonly IModelQueryProvider _provider;

    // The root: its expression is a constant pointing at itself, which the provider swaps for EF Core's
    // real source at execution time.
    internal ModelQueryable(IModelQueryProvider provider)
    {
        _provider = provider;
        Expression = Expression.Constant(this, typeof(IQueryable<TElement>));
    }

    internal ModelQueryable(IModelQueryProvider provider, Expression expression)
    {
        _provider = provider;
        Expression = expression;
    }

    public Type ElementType => typeof(TElement);

    public Expression Expression { get; }

    public IQueryProvider Provider => _provider;

    public IEnumerator<TElement> GetEnumerator() => _provider.Materialize<TElement>(Expression).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IAsyncEnumerator<TElement> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        _provider.Stream<TElement>(Expression, cancellationToken).GetAsyncEnumerator(cancellationToken);
}

/// <summary>The execution half a <see cref="ModelQueryable{TElement}" /> calls back into.</summary>
internal interface IModelQueryProvider : IAsyncQueryProvider
{
    List<TElement> Materialize<TElement>(Expression expression);

    IAsyncEnumerable<TElement> Stream<TElement>(Expression expression, CancellationToken cancellationToken);
}

/// <summary>
///     Runs a model queryable's expression against a context opened for that one execution.
/// </summary>
/// <remarks>
///     <para>
///         Composition never touches a database: <c>Queryable.Where</c>, <c>OrderBy</c>, <c>Skip</c> and
///         the rest only build a new expression over the root. Execution binds it — the root constant is
///         replaced by the model query applied to a fresh context's no-tracking set, the result is produced
///         by EF Core's own provider, and the context is disposed.
///     </para>
///     <para>
///         Generic throughout, so nothing reflects over the element type — the grid's synchronous
///         <c>Count()</c>/<c>ToList()</c> and EF Core's awaited operators both stay trim- and AOT-safe.
///     </para>
/// </remarks>
internal sealed class ModelQueryProvider<TEntity> : IModelQueryProvider
    where TEntity : Model
{
    private readonly ModelQuery<TEntity> _query;
    private readonly ModelQueryable<TEntity> _root;

    internal ModelQueryProvider(ModelQuery<TEntity> query)
    {
        _query = query;
        _root = new ModelQueryable<TEntity>(this);
    }

    internal IQueryable<TEntity> Root => _root;

    public IQueryable CreateQuery(Expression expression) =>
        throw new NotSupportedException(
            "A model queryable is composed through the generic LINQ operators (Queryable.Where, OrderBy, " +
            "Skip, …); the non-generic IQueryProvider.CreateQuery is not supported.");

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return new ModelQueryable<TElement>(this, expression);
    }

    public object? Execute(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var (context, provider, bound) = Bind(expression);
        using (context)
        {
            return provider.Execute(bound);
        }
    }

    public TResult Execute<TResult>(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var (context, provider, bound) = Bind(expression);
        using (context)
        {
            return provider.Execute<TResult>(bound);
        }
    }

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var (context, provider, bound) = Bind(expression);

        TResult result;
        try
        {
            result = provider.ExecuteAsync<TResult>(bound, cancellationToken);
        }
        catch
        {
            context.Dispose();
            throw;
        }

        // CountAsync, FirstOrDefaultAsync and the rest hand back a Task. The context has to outlive it, so
        // it is released by a continuation — on the non-generic Task, which needs no reflection over the
        // result type.
        if (result is Task task)
        {
            _ = task.ContinueWith(
                static (_, state) => ((DbContext)state!).Dispose(),
                context,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return result;
        }

        context.Dispose();
        throw new NotSupportedException(
            "Only a Task-returning asynchronous operator (CountAsync, FirstOrDefaultAsync, …) can run against " +
            "a model queryable; stream rows with AsAsyncEnumerable() or ToListAsync() instead.");
    }

    public List<TElement> Materialize<TElement>(Expression expression)
    {
        var (context, provider, bound) = Bind(expression);
        using (context)
        {
            // Materialised before the context goes: a lazily enumerated result would reach a disposed one.
            return provider.CreateQuery<TElement>(bound).ToList();
        }
    }

    public async IAsyncEnumerable<TElement> Stream<TElement>(
        Expression expression,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (context, provider, bound) = Bind(expression);
        await using var owned = context;

        await foreach (var element in provider.CreateQuery<TElement>(bound)
                           .AsAsyncEnumerable()
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return element;
        }
    }

    private Bound Bind(Expression expression)
    {
        var context = Db.CreateContext();
        try
        {
            var source = _query.Apply(context.Set<TEntity>());
            var bound = new RootBinder(_root, source.Expression).Visit(expression);
            return new Bound(context, (IAsyncQueryProvider)source.Provider, bound);
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private readonly record struct Bound(DbContext Context, IAsyncQueryProvider Provider, Expression Expression);

    private sealed class RootBinder(object root, Expression source) : ExpressionVisitor
    {
        protected override Expression VisitConstant(ConstantExpression node) =>
            ReferenceEquals(node.Value, root) ? source : node;
    }
}
