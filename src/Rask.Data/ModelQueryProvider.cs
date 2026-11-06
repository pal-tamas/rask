using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Rask.Data;

/// <summary>
///     Runs a model queryable's expression against a context opened for that one execution.
/// </summary>
/// <remarks>
///     <para>
///         Composition never touches a database: <c>Queryable.Where</c>, <c>OrderBy</c>, <c>Skip</c> and
///         the rest only build a new expression over the root. Execution binds it — the root, and the root of
///         every other model query nested in it, is replaced by that query applied to the one fresh context's
///         no-tracking set (<see cref="ModelQueryBinder" />), the result is produced by EF Core's own provider,
///         and the context is disposed.
///     </para>
///     <para>
///         Generic throughout, so nothing reflects over the element type — the grid's synchronous
///         <c>Count()</c>/<c>ToList()</c> and EF Core's awaited operators both stay trim- and AOT-safe.
///     </para>
/// </remarks>
internal sealed class ModelQueryProvider<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity> : IModelQueryProvider
    where TEntity : class
{
    private readonly ModelQuery<TEntity> _query;

    internal ModelQueryProvider(ModelQuery<TEntity> query)
    {
        _query = query;
        Root = new ModelQueryable<TEntity>(this);
    }

    internal IQueryable<TEntity> Root { get; }

    public Type ElementType => typeof(TEntity);

    public Expression SourceIn(DbContext context) => _query.Apply(context.Set<TEntity>()).Expression;

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
        await using var owned = context.ConfigureAwait(false);

        await foreach (var element in provider.CreateQuery<TElement>(bound)
                           .AsAsyncEnumerable()
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return element;
        }
    }

    private static Bound Bind(Expression expression)
    {
        var context = ReadDb.OpenFor<TEntity>();
        try
        {
            var provider = (IAsyncQueryProvider)((IQueryable)context.Set<TEntity>()).Provider;
            var bound = new ModelQueryBinder(context).Visit(expression);
            return new Bound(context, provider, bound);
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private readonly record struct Bound(DbContext Context, IAsyncQueryProvider Provider, Expression Expression);
}
