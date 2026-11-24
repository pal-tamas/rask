using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>The reads a declared lock makes of the parents a save touches (<see cref="ParentLock{T,TParent}" />).</summary>
/// <typeparam name="TParent">The parent.</typeparam>
internal static class ParentRead<[DynamicallyAccessedMembers(DataTrimming.Entity)] TParent>
    where TParent : class
{
    /// <summary>The parents of <paramref name="keys" />, past every query filter.</summary>
    internal static IQueryable<TParent> Touched(DbContext context, IProperty key, IReadOnlyList<object> keys)
    {
        var row = Expression.Parameter(typeof(TParent), "p");
        var stored = StoredProperty.Of(row, key);
        return context.Set<TParent>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(Expression.Lambda<Func<TParent, bool>>(AnyOf(stored, key.ClrType, keys, 0, keys.Count), row));
    }

    /// <summary>
    ///     The read that takes the update lock on the parents of <paramref name="keys" />: a bare <c>SELECT 1</c>, so the
    ///     parents' table is the first a rewrite finds (<see cref="ForUpdateSql" />).
    /// </summary>
    internal static IQueryable<int> ForUpdate(DbContext context, IProperty key, IReadOnlyList<object> keys) =>
        Touched(context, key, keys).TagWith(ForUpdateSql.Tag).Select(static p => 1);

    // key = @a OR key = @b …, split in halves so a long list makes a shallow tree.
    private static BinaryExpression AnyOf(Expression stored, Type type, IReadOnlyList<object> keys, int from, int count) =>
        count == 1
            ? Expression.Equal(stored, QueryParameter.Of(keys[from], type))
            : Expression.OrElse(
                AnyOf(stored, type, keys, from, count / 2),
                AnyOf(stored, type, keys, from + (count / 2), count - (count / 2)));
}
