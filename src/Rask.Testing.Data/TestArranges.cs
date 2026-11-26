using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Rask.Data;
using Rask.Wire;

namespace Rask.Testing;

/// <summary>
///     <c>await database.Arrange&lt;Period&gt;(…)</c> and <c>await database.ArrangeGone&lt;User&gt;(…)</c>: a test's setup
///     of a state the aggregate itself would never make — a legacy row, a user moved to another tenant, a closed period
///     under a note already written — as one statement, straight to the table.
/// </summary>
/// <remarks>
///     <para>
///         Setup, never the write under test: no tenant or soft-delete filter (every tenant's rows are matched), no
///         <c>LockedBy</c>, no <c>UpdatedAt</c> stamp or <c>Version</c> bump, no data-changed notice, and nothing counted
///         toward <c>manyAggregates:</c>. What the app does is written through the aggregate's own writes —
///         <c>X.UpdateWhere(…)</c>, <c>X.DeleteWhere(…)</c>, its entries — which keep all of that.
///     </para>
///     <para>
///         The statement runs on <see cref="TestDatabase.Context" />, which then reads what the database has: a row it
///         tracks takes the arranged values on its next read. EF Core's own <c>ExecuteUpdateAsync</c> in a test is
///         RASK187; these are not, as they say they are setup.
///     </para>
/// </remarks>
public static class TestArranges
{
    extension(TestDatabase database)
    {
        /// <summary>
        ///     Sets columns of every <typeparamref name="T" /> row <paramref name="where" /> matches, in any tenant, as one
        ///     statement — no rule, lock, stamp or notice: <c>await database.Arrange&lt;Period&gt;(p =&gt; p.Id == id,
        ///     set =&gt; set.SetProperty(p =&gt; p.Closed, true));</c>.
        /// </summary>
        /// <typeparam name="T">The aggregate whose rows are arranged.</typeparam>
        /// <param name="where">The rows to change.</param>
        /// <param name="set">The columns to set, as EF Core's <c>ExecuteUpdate</c> takes them.</param>
        /// <param name="cancellationToken">Cancels the statement; unset, the test's.</param>
        /// <returns>How many rows were changed.</returns>
        public Task<int> Arrange<T>(
            Expression<Func<T, bool>> where, Action<UpdateSettersBuilder<T>> set, CancellationToken cancellationToken = default)
            where T : class, IAggregate
        {
            ArgumentNullException.ThrowIfNull(set);
            return Rows(database, where).ExecuteUpdateAsync(set, Ambient.Or(cancellationToken));
        }

        /// <summary>
        ///     Deletes every <typeparamref name="T" /> row <paramref name="where" /> matches, in any tenant, as one
        ///     statement — removed, even from a soft-deleted aggregate, and nothing asked:
        ///     <c>await database.ArrangeGone&lt;User&gt;(u =&gt; u.Id == anna);</c>.
        /// </summary>
        /// <typeparam name="T">The aggregate whose rows are removed.</typeparam>
        /// <param name="where">The rows to remove.</param>
        /// <param name="cancellationToken">Cancels the statement; unset, the test's.</param>
        /// <returns>How many rows were removed.</returns>
        public Task<int> ArrangeGone<T>(Expression<Func<T, bool>> where, CancellationToken cancellationToken = default)
            where T : class, IAggregate =>
            Rows(database, where).ExecuteDeleteAsync(Ambient.Or(cancellationToken));
    }

    private static IQueryable<T> Rows<T>(TestDatabase database, Expression<Func<T, bool>> where)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(where);
        return database.Context.Set<T>().IgnoreQueryFilters().Where(where);
    }
}
