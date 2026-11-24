using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     An index over some rows only, declared once in C# for every database:
///     <c>b.HasIndex(x =&gt; x.Partner).IsUnique("…").Where(x =&gt; x.RevokedAt == null)</c>.
/// </summary>
/// <remarks>
///     <para>
///         Written as the provider's own filter as the model is finalized — a filtered index on SQL Server, a partial
///         index on PostgreSQL and on SQLite. A unique one then holds among the rows the filter keeps: one live grant
///         per partner, however many revoked ones.
///     </para>
///     <para>
///         The filter is the plainest condition, because SQL Server's filtered index takes no other: columns compared
///         with constants (or null), joined by <c>&amp;&amp;</c>, and a boolean column as itself or with <c>!</c>.
///         <c>||</c>, two columns compared, and a <c>!=</c> on a nullable column — which C# would keep the nulls for —
///         fail the model build, naming the term. A constant is written as its column stores it: an enum, a
///         strongly-typed id, a date.
///     </para>
///     <para>
///         On SQL Server a unique index also leaves out the rows with a null in a nullable key column, as EF Core's own
///         unique index there does — NULLs never collide, as on PostgreSQL and SQLite.
///     </para>
/// </remarks>
public static class FilteredIndexBuilderExtensions
{
    /// <summary>Makes the index hold only the rows <paramref name="rows" /> keeps.</summary>
    /// <typeparam name="TEntity">The entity the index is on.</typeparam>
    /// <param name="index">The index.</param>
    /// <param name="rows">The rows it holds: <c>x =&gt; x.RevokedAt == null</c>.</param>
    /// <returns>The same builder.</returns>
    public static IndexBuilder<TEntity> Where<TEntity>(this IndexBuilder<TEntity> index, Expression<Func<TEntity, bool>> rows)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(rows);

        index.Metadata.SetAnnotation(FilteredIndexConvention.Annotation, new DeclaredExpression(rows, $"Where({rows})"));
        return index;
    }
}
