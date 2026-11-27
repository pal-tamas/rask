using System.Linq.Expressions;

namespace Rask.Data;

/// <summary>
///     A rule <see cref="NoOverlapBuilderExtensions" /> declared, still open to say which rows it holds:
///     <c>b.NoOverlap(x =&gt; x.Code, x =&gt; x.Period, "…").Where(x =&gt; x.DriverId != null)</c>.
/// </summary>
/// <typeparam name="TEntity">The entity the rule is declared on.</typeparam>
public sealed class NoOverlapBuilder<TEntity>
    where TEntity : class
{
    private readonly NoOverlapDeclaration _declaration;

    internal NoOverlapBuilder(NoOverlapDeclaration declaration) => _declaration = declaration;

    /// <summary>
    ///     Holds the rule over the rows <paramref name="rows" /> keeps only: another row it leaves out never conflicts, and
    ///     a row it leaves out is never refused.
    /// </summary>
    /// <param name="rows">
    ///     The rows the rule holds, in the subset a filtered index takes — the entity's columns compared with constants,
    ///     joined by <c>&amp;&amp;</c>: <c>x =&gt; x.DriverId != null</c>.
    /// </param>
    /// <returns>The same rule.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows" /> is null.</exception>
    public NoOverlapBuilder<TEntity> Where(Expression<Func<TEntity, bool>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        _declaration.Rows = new DeclaredExpression(rows, $"NoOverlap(…).Where({rows})");
        return this;
    }
}
