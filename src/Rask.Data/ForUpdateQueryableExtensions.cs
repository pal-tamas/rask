using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>The <c>ForUpdate</c> step on any EF Core query: the read face's own, for a set named by its type.</summary>
public static class ForUpdateQueryableExtensions
{
    /// <summary>
    ///     Reads the rows under an update lock, held to the end of the <c>Db.Transaction</c> the read runs in — for code
    ///     generic over the entity, which has no read face to call it on:
    ///     <c>await db.Set&lt;TRefund&gt;().Where(r =&gt; r.Id == id).ForUpdate().ToListAsync()</c>.
    /// </summary>
    /// <typeparam name="T">The entity read.</typeparam>
    /// <param name="source">The query whose rows are locked.</param>
    /// <returns>The same query, read under the lock.</returns>
    /// <remarks>
    ///     <para>
    ///         The same lock as <see cref="ModelQuery{TEntity}.ForUpdate" />, in each database's own words:
    ///         <c>WITH (UPDLOCK, HOLDLOCK)</c> on the query's first table on SQL Server, <c>FOR UPDATE OF</c> it on
    ///         PostgreSQL, and as it is on SQLite, whose one writer serializes the transaction already. The rows of a joined
    ///         table (an <c>Include</c>) are not locked. The query runs on a Rask context — a <see cref="RaskDbContext" />,
    ///         or one built with <c>AddRaskData()</c>'s interceptors — which is what writes the lock.
    ///     </para>
    ///     <para>
    ///         It locks the rows a read hands back, so a <c>Count</c>, a <c>LongCount</c>, an <c>Any</c> or an <c>All</c>
    ///         of it is refused as the query is compiled — PostgreSQL locks no rows of an aggregate. Read the rows, and count
    ///         them in memory.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">No <c>Db.Transaction</c> is in progress.</exception>
    public static IQueryable<T> ForUpdate<T>(this IQueryable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        GeneratedLocks.DemandTransaction($"{typeof(T).Name} rows");
        return source.TagWith(ForUpdateSql.Tag);
    }
}
