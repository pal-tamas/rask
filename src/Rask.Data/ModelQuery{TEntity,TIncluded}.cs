using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Rask.Data;

/// <summary>
///     A <see cref="ModelQuery{TEntity}" /> just after an <c>Include</c>: everything the query does, and a
///     <see cref="ThenInclude{TNext}" /> that loads a navigation of what was just included.
/// </summary>
/// <remarks>
///     <code>
/// var fuelings = await Fueling.Include(f =&gt; f.Vehicle).ThenInclude(v =&gt; v.Owner);
/// var orders = await Order.Include(o =&gt; o.Lines).ThenInclude(l =&gt; l.Product);   // a list, then each row's navigation
///     </code>
///     As EF Core's <c>ThenInclude</c>, it follows the <c>Include</c> (or the <c>ThenInclude</c>) it continues and nothing
///     else: any other operator hands back the plain query.
/// </remarks>
/// <typeparam name="TEntity">The rows queried.</typeparam>
/// <typeparam name="TIncluded">The navigation just included: a read face, or a list of them.</typeparam>
public sealed class ModelQuery<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TIncluded> : ModelQuery<TEntity>
    where TEntity : class
{
    internal ModelQuery(Func<IQueryable<TEntity>, IQueryable<TEntity>> compose, bool ordered, bool unrankedSearch, bool forUpdate)
        : base(compose, ordered, unrankedSearch, forUpdate)
    {
    }

    /// <summary>Eager-loads <paramref name="navigation" /> of the row just included.</summary>
    /// <typeparam name="TNext">The navigation's type.</typeparam>
    /// <param name="navigation">The navigation — <c>v =&gt; v.Owner</c>.</param>
    /// <returns>The query, which another <c>ThenInclude</c> continues.</returns>
    public ModelQuery<TEntity, TNext> ThenInclude<TNext>(Expression<Func<TIncluded, TNext>> navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        return Including<TNext>(q => ((IIncludableQueryable<TEntity, TIncluded>)q).ThenInclude(navigation));
    }

    // Each row of a list just included: EF Core's ThenInclude over the IEnumerable its includable query is covariant to.
    internal ModelQuery<TEntity, TNext> ThenIncludeEach<TElement, TNext>(Expression<Func<TElement, TNext>> navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        return Including<TNext>(q => ((IIncludableQueryable<TEntity, IEnumerable<TElement>>)q).ThenInclude(navigation));
    }
}
