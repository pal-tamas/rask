using System.Diagnostics.CodeAnalysis;

namespace Rask.Data;

/// <summary>
///     Numbers a <see cref="NumberedBuilderExtensions.Numbered{TEntity, TNumber, TWithin, TOrder}" /> numbering anew by hand,
///     on the type itself: <c>await Fueling.Renumber(pistolId)</c>, <c>await Fueling.RenumberAll()</c>.
/// </summary>
/// <remarks>
///     <para>
///         A save keeps the numbers by itself, so an app needs this only for rows written past a save — a raw
///         <c>ExecuteUpdate</c>, a migration's SQL, an import straight into the table, rows stored before the numbering was
///         declared. Each group is numbered whole, from its first row, by the rules a save numbers it by (<c>by</c>,
///         <c>skip</c>, <c>Same</c>, <c>SkipWhen</c>), and only the numbers that changed are written.
///     </para>
///     <para>
///         It runs in a <c>Db.Transaction</c> of its own, or joins the one in progress, holding the lock a save takes on each
///         group (SQL Server's <c>sp_getapplock</c>, PostgreSQL's <c>pg_advisory_xact_lock</c>; SQLite has one writer), so a
///         save into a group being numbered waits for it. The tenant filter applies: a tenant's call numbers its own rows.
///         The pages showing the rows are told once it commits.
///     </para>
/// </remarks>
public static class Renumbering
{
    extension<[DynamicallyAccessedMembers(DataTrimming.Entity)] T>(T)
        where T : class, IEntity
    {
        /// <summary>
        ///     Numbers the rows of one group anew, as a save that touched its first row would:
        ///     <c>await Fueling.Renumber(pistolId)</c>.
        /// </summary>
        /// <param name="group">The group, a value of the type the numbering's <c>within</c> names.</param>
        /// <param name="cancellationToken">Cancels it; unset, the work this call belongs to does.</param>
        /// <returns>How many rows' numbers changed.</returns>
        /// <exception cref="InvalidOperationException"><typeparamref name="T" /> declares no numbering.</exception>
        /// <exception cref="ArgumentException"><paramref name="group" /> is not of the type the rows are counted within.</exception>
#pragma warning disable CA1000 // the entity's own static is the DX
        public static Task<int> Renumber(object group, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(group);
            return Numberings.Mend(typeof(T), [group], cancellationToken);
        }

        /// <summary>Numbers the rows of each of <paramref name="groups" /> anew: <c>await Fueling.Renumber(pistolIds)</c>.</summary>
        /// <param name="groups">The groups, values of the type the numbering's <c>within</c> names.</param>
        /// <param name="cancellationToken">Cancels it; unset, the work this call belongs to does.</param>
        /// <returns>How many rows' numbers changed.</returns>
        /// <exception cref="InvalidOperationException"><typeparamref name="T" /> declares no numbering.</exception>
        /// <exception cref="ArgumentException">A group is null, or not of the type the rows are counted within.</exception>
        public static Task<int> Renumber(IEnumerable<object> groups, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(groups);
            List<object> distinct = [.. groups.Distinct()];
            return distinct.Contains(null!)
                ? throw new ArgumentException("A group to renumber is null; a row in no group has no number.", nameof(groups))
                : Numberings.Mend(typeof(T), distinct, cancellationToken);
        }

        /// <summary>
        ///     Numbers every group anew, and takes the number of a row in none — the repair after rows were written past the
        ///     numbering: <c>await Fueling.RenumberAll()</c>.
        /// </summary>
        /// <param name="cancellationToken">Cancels it; unset, the work this call belongs to does.</param>
        /// <returns>How many rows' numbers changed.</returns>
        /// <exception cref="InvalidOperationException"><typeparamref name="T" /> declares no numbering.</exception>
        public static Task<int> RenumberAll(CancellationToken cancellationToken = default) =>
            Numberings.Mend(typeof(T), groups: null, cancellationToken);
#pragma warning restore CA1000
    }
}
