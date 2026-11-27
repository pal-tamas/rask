using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     The save of a context Rask opened for one write — <c>order.Save()</c>, <c>X.Update</c>, <c>X.Delete</c>, an
///     operation's entry, an import's batch — as ONE unit: what the save reads first (a <c>LockedBy</c> parent under an
///     update lock, a declared rule's check, a numbering's lock) and what it writes share one transaction.
/// </summary>
/// <remarks>
///     <para>
///         A context with no retrying execution strategy opens that transaction inside the save
///         (<see cref="SaveTransaction" />). A retrying one refuses a transaction it did not start, so here the save runs
///         inside <c>strategy.ExecuteAsync</c> with a transaction of its own — EF Core's pattern for a user transaction under
///         a retrying strategy — and a transient failure retries the whole unit: the lock read, the checks and the write.
///     </para>
///     <para>
///         Each attempt saves without accepting its changes, so one that fails (the commit included) leaves the rows
///         still to be written; its rollback takes back what the attempt stamped on them (<see cref="AttemptUndo" />,
///         and the domain events by <see cref="DomainEventInterceptor" />). The changes are accepted once it commits.
///     </para>
///     <para>
///         Inside <c>Db.Transaction</c> the strategy is the block's, so this is a plain save of a context that joins it.
///     </para>
/// </remarks>
internal static class SaveUnit
{
    /// <summary>Saves <paramref name="context" /> as one unit; see <see cref="SaveUnit" />.</summary>
    internal static async Task SaveAsync(DbContext context, CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        if (!strategy.RetriesOnFailure || context.Database.CurrentTransaction is not null)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await strategy.ExecuteAsync(
            context,
            static (_, saving, token) => AttemptAsync(saving, token),
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);

        context.ChangeTracker.AcceptAllChanges();
        AggregateSave.RememberSaved(context);
    }

    private static async Task<int> AttemptAsync(DbContext context, CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var transactionScope = transaction.ConfigureAwait(false);
        try
        {
            var written = await context.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return written;
        }
        catch
        {
            await RollBackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    // An explicit rollback, which is what tells AttemptUndo and the domain events to take this attempt back. After a commit
    // that failed the connection may be gone too: what failed first is the one worth throwing.
    private static async Task RollBackAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (DbException)
        {
            // Rolled back by the database when the connection went: nothing left to take back here.
        }
        catch (InvalidOperationException)
        {
            // The connection is closed already, which rolled the transaction back with it.
        }
    }
}
