using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Rask.Data;

/// <summary>
///     <c>ThenInclude</c> after an <c>Include</c> of a list: a navigation of each row in it — a read face's children, or
///     the rows that point at it.
/// </summary>
/// <remarks>
///     <code>
/// var orders = await Order.Include(o =&gt; o.Lines).ThenInclude(l =&gt; l.Product);
///     </code>
/// </remarks>
public static class ModelQueryIncludeExtensions
{
    /// <summary>Eager-loads <paramref name="navigation" /> of each row of the children just included.</summary>
    /// <typeparam name="TEntity">The rows queried.</typeparam>
    /// <typeparam name="TElement">A row of the list just included.</typeparam>
    /// <typeparam name="TNext">The navigation's type.</typeparam>
    /// <param name="query">The query, just after the <c>Include</c>.</param>
    /// <param name="navigation">The navigation — <c>l =&gt; l.Product</c>.</param>
    /// <returns>The query, which another <c>ThenInclude</c> continues.</returns>
    public static ModelQuery<TEntity, TNext> ThenInclude<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TElement, TNext>(
        this ModelQuery<TEntity, List<TElement>> query, Expression<Func<TElement, TNext>> navigation)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.ThenIncludeEach<TElement, TNext>(navigation);
    }

    /// <summary>Eager-loads <paramref name="navigation" /> of each of the rows just included that point here.</summary>
    /// <typeparam name="TEntity">The rows queried.</typeparam>
    /// <typeparam name="TElement">A row of the list just included.</typeparam>
    /// <typeparam name="TNext">The navigation's type.</typeparam>
    /// <param name="query">The query, just after the <c>Include</c>.</param>
    /// <param name="navigation">The navigation — <c>d =&gt; d.ToFueling</c>.</param>
    /// <returns>The query, which another <c>ThenInclude</c> continues.</returns>
    public static ModelQuery<TEntity, TNext> ThenInclude<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TElement, TNext>(
        this ModelQuery<TEntity, IReadOnlyList<TElement>> query, Expression<Func<TElement, TNext>> navigation)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.ThenIncludeEach<TElement, TNext>(navigation);
    }
}
