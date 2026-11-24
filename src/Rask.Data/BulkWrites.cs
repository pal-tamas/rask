using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
///     The writes over many rows of one aggregate type, on the type itself: <c>Pairing.DeleteWhere(…)</c>,
///     <c>Period.UpdateWhere(…)</c>, <c>Fueling.UpdateEach(…)</c>, <c>Fueling.Upsert(…)</c>, <c>Transaction.Import(…)</c>.
/// </summary>
/// <remarks>
///     <para>
///         Two shapes, for two needs. <b>A set write</b> — <c>UpdateWhere</c>, <c>DeleteWhere</c> — is one statement over
///         every row a predicate matches: no row is loaded, so the aggregate's own methods and <c>Validate()</c> do not
///         run, and it is for what no rule is about (a flag, a stamp, a session row). It still keeps what a save keeps:
///         the tenant and soft-delete filters, <c>UpdatedAt</c> and <c>Version</c>, the declared locks (<c>LockedBy</c>),
///         the unique and referenced-row messages, the data-changed notices. <b>A load per row</b> — <c>UpdateEach</c>,
///         <c>Upsert</c>, <c>Import</c> — goes through the aggregate: its methods, its rules, its events.
///     </para>
///     <para>
///         Every one runs in a <c>Db.Transaction</c> of its own, or joins the one in progress. A set write counts as one
///         write of its type (<c>Pairing (set)</c>) toward <see cref="RaskDataOptions.OneAggregatePerTransaction" />; a
///         batch of <c>UpdateEach</c> or <c>Import</c> says why it writes many — each row is its own aggregate — and is let
///         through.
///     </para>
/// </remarks>
public static class BulkWrites
{
    extension<[DynamicallyAccessedMembers(DataTrimming.Entity)] T>(T)
        where T : class, IAggregate
    {
        /// <summary>
        ///     Deletes every row <paramref name="where" /> matches, in one statement — a soft-deleted aggregate's stamped
        ///     instead — and tells the pages showing them: <c>await Pairing.DeleteWhere(p =&gt; p.Pump == pumpId)</c>.
        /// </summary>
        /// <param name="where">Which rows, translated to the database's SQL; the tenant and soft-delete filters still apply.</param>
        /// <param name="cancellationToken">Cancels the delete; unset, the work this call belongs to does.</param>
        /// <returns>How many rows were deleted.</returns>
        /// <exception cref="RaskValidationException">A row's parent is locked (<c>LockedBy</c>), or another row still refers to one.</exception>
        /// <exception cref="InvalidOperationException">
        ///     The aggregate names neither Deletable nor SoftDelete, or it — or a child it holds — declares a rule a statement
        ///     cannot ask (<c>ValidateDelete()</c>, a child's <c>LockedBy</c>): delete those one by one through <c>X.Delete(id)</c>.
        /// </exception>
#pragma warning disable CA1000 // the aggregate's own static is the DX
        public static Task<int> DeleteWhere(Expression<Func<T, bool>> where, CancellationToken cancellationToken = default) =>
            BulkSetWrite.Delete(where, cancellationToken);

        /// <summary>
        ///     Changes every row <paramref name="where" /> matches, in one statement, stamping <c>UpdatedAt</c> and bumping
        ///     <c>Version</c> as a save would: <c>await Period.UpdateWhere(p =&gt; p.Id == id, s =&gt; s.SetProperty(p =&gt; p.Closed, true))</c>.
        /// </summary>
        /// <param name="where">Which rows, translated to the database's SQL; the tenant and soft-delete filters still apply.</param>
        /// <param name="set">The columns to set, as EF Core's <c>ExecuteUpdate</c> takes them.</param>
        /// <param name="cancellationToken">Cancels the update; unset, the work this call belongs to does.</param>
        /// <returns>How many rows were changed.</returns>
        /// <remarks>
        ///     No row is loaded, so the aggregate's <c>Validate()</c> is not asked: a change a rule is about goes through
        ///     <c>UpdateEach</c>, which loads each one. Its locks are asked — of the parent a row has, and the one it is moved to.
        /// </remarks>
        /// <exception cref="RaskValidationException">A row's parent is locked (<c>LockedBy</c>), or a unique index refuses the change.</exception>
        public static Task<int> UpdateWhere(
            Expression<Func<T, bool>> where, Action<UpdateSettersBuilder<T>> set, CancellationToken cancellationToken = default) =>
            BulkSetWrite.Update(where, set, cancellationToken);

        /// <summary>
        ///     Loads every aggregate <paramref name="where" /> matches, changes it through its own method and saves it — its
        ///     rules, events and notices as for one — <paramref name="batchSize" /> to a transaction:
        ///     <c>await Fueling.UpdateEach(f =&gt; codes.Contains(f.DriverChip), f =&gt; f.Match(plan))</c>.
        /// </summary>
        /// <param name="where">Which aggregates: the ones that match when it starts, in key order.</param>
        /// <param name="apply">The change, through the aggregate's own method.</param>
        /// <param name="batchSize">How many aggregates one transaction saves; 1 to 10 000.</param>
        /// <param name="cancellationToken">Cancels the rest; unset, the work this call belongs to does.</param>
        /// <returns>How many aggregates were saved.</returns>
        /// <remarks>
        ///     A refused batch rolls back alone and the refusal is thrown; the batches before it stay committed, so the change
        ///     should be one that can run twice. Inside a <c>Db.Transaction</c> it joins that one instead.
        /// </remarks>
        /// <exception cref="RaskValidationException">An aggregate's rules refused the change.</exception>
        public static Task<int> UpdateEach(
            Expression<Func<T, bool>> where, Action<T> apply, int batchSize = 500, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(apply);
            return BulkEach.Update<T>(where, aggregate =>
            {
                apply(aggregate);
                return Task.CompletedTask;
            }, batchSize, cancellationToken);
        }

        /// <summary>
        ///     Loads every aggregate <paramref name="where" /> matches, changes it through its own asynchronous method and saves
        ///     it, <paramref name="batchSize" /> to a transaction — as the synchronous overload does.
        /// </summary>
        /// <param name="where">Which aggregates: the ones that match when it starts, in key order.</param>
        /// <param name="apply">The change, through the aggregate's own method.</param>
        /// <param name="batchSize">How many aggregates one transaction saves; 1 to 10 000.</param>
        /// <param name="cancellationToken">Cancels the rest; unset, the work this call belongs to does.</param>
        /// <returns>How many aggregates were saved.</returns>
        /// <exception cref="RaskValidationException">An aggregate's rules refused the change.</exception>
        public static Task<int> UpdateEach(
            Expression<Func<T, bool>> where, Func<T, Task> apply, int batchSize = 500, CancellationToken cancellationToken = default) =>
            BulkEach.Update(where, apply, batchSize, cancellationToken);

        /// <summary>
        ///     Changes the one aggregate <paramref name="match" /> finds through <paramref name="change" />, or saves a new one
        ///     <paramref name="create" /> builds when none matches — in one transaction, its rules asked:
        ///     <c>await Fueling.Upsert(f =&gt; f.Pump == r.Pump &amp;&amp; f.Number == r.Number, () =&gt; Fueling.Taken(r), f =&gt; f.AsThePumpHoldsIt(r.Moment))</c>.
        /// </summary>
        /// <param name="match">What makes one unique — back it with a unique index, so two at once cannot both make one.</param>
        /// <param name="create">Builds the new one, through the aggregate's factory.</param>
        /// <param name="change">Changes the stored one, through its own method.</param>
        /// <param name="cancellationToken">Cancels it; unset, the work this call belongs to does.</param>
        /// <returns>The aggregate as saved, and whether it was made new.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="match" /> matched more than one.</exception>
        /// <exception cref="RaskValidationException">Its rules refused it, or the unique index did.</exception>
        public static Task<Upserted<T>> Upsert(
            Expression<Func<T, bool>> match, Func<T> create, Action<T> change, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(create);
            ArgumentNullException.ThrowIfNull(change);
            return BulkUpsert.Run<T>(match, () => Task.FromResult(create()), aggregate =>
            {
                change(aggregate);
                return Task.CompletedTask;
            }, cancellationToken);
        }

        /// <summary>
        ///     Changes the one aggregate <paramref name="match" /> finds, or saves a new one, as the synchronous overload does —
        ///     through an asynchronous factory and method.
        /// </summary>
        /// <param name="match">What makes one unique — back it with a unique index.</param>
        /// <param name="create">Builds the new one.</param>
        /// <param name="change">Changes the stored one.</param>
        /// <param name="cancellationToken">Cancels it; unset, the work this call belongs to does.</param>
        /// <returns>The aggregate as saved, and whether it was made new.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="match" /> matched more than one.</exception>
        /// <exception cref="RaskValidationException">Its rules refused it, or the unique index did.</exception>
        public static Task<Upserted<T>> Upsert(
            Expression<Func<T, bool>> match, Func<Task<T>> create, Func<T, Task> change, CancellationToken cancellationToken = default) =>
            BulkUpsert.Run(match, create, change, cancellationToken);

        /// <summary>
        ///     Imports <paramref name="rows" />: each made by <paramref name="make" /> — the aggregate's factory — and saved
        ///     through its rules, a batch to a transaction, with a fresh change tracker for each:
        ///     <c>var result = await Transaction.Import(lines, line =&gt; Loaded(file, line), o =&gt; o.OnInvalid = ImportFailure.Collect)</c>.
        /// </summary>
        /// <typeparam name="TRow">The rows: a file's lines, an API's records.</typeparam>
        /// <param name="rows">The rows, read once, a batch at a time.</param>
        /// <param name="make">Builds the aggregate of one row; a <see cref="RaskValidationException" /> from it refuses the row.</param>
        /// <param name="configure">
        ///     <see cref="ImportOptions{T, TRow}.BatchSize" />, <see cref="ImportOptions{T, TRow}.OnInvalid" />, and
        ///     <see cref="ImportOptions{T, TRow}.Upsert{TKey}" /> for rows that may meet what an earlier import stored.
        /// </param>
        /// <param name="cancellationToken">Cancels the rest; unset, the work this call belongs to does.</param>
        /// <returns>How many were inserted and updated, and the rows set aside with why.</returns>
        /// <exception cref="RaskValidationException">A row was refused, and <see cref="ImportOptions{T, TRow}.OnInvalid" /> is <see cref="ImportFailure.Stop" />.</exception>
        /// <exception cref="InvalidOperationException"><see cref="ImportFailure.Collect" /> asked inside a <c>Db.Transaction</c>.</exception>
        public static Task<ImportResult<TRow>> Import<TRow>(
            IEnumerable<TRow> rows, Func<TRow, T> make, Action<ImportOptions<T, TRow>>? configure = null,
            CancellationToken cancellationToken = default) =>
            BulkImport.Run(rows, make, configure, cancellationToken);
#pragma warning restore CA1000
    }
}
