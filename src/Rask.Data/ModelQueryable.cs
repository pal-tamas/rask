using System.Collections;
using System.Linq.Expressions;

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
