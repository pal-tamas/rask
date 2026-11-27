using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     A transaction opened for ONE save, before it starts, so what the save reads first — a declared rule's check, a
///     parent read under an update lock — and what it then writes are one transaction. Committed when the save ends,
///     rolled back when it fails or never starts.
/// </summary>
/// <remarks>
///     Opened only when the caller has none: a transaction of the caller's (or the <c>Db.Transaction</c> the context joins
///     at its first use) already holds the save. A context whose execution strategy retries is the exception — EF Core
///     refuses a transaction it did not start there — so Rask's own writes save such a context as one unit inside the
///     strategy (<see cref="SaveUnit" />), whose transaction this finds; only a caller's own <c>SaveChanges</c> on one,
///     outside <c>Db.Transaction</c>, still runs the reads just before the save, outside a transaction.
/// </remarks>
internal static class SaveTransaction
{
    private static readonly ConditionalWeakTable<DbContext, IDbContextTransaction> Opened = [];

    /// <summary>Opens one on <paramref name="context" /> unless it is in one already.</summary>
    internal static async Task Open(DbContext context, CancellationToken cancellationToken)
    {
        if (Opened.TryGetValue(context, out _) ||
            AmbientTransaction.JoinBeforeUse(context) ||
            context.Database.CurrentTransaction is not null ||
            context.Database.CreateExecutionStrategy().RetriesOnFailure)
        {
            return;
        }

        Opened.AddOrUpdate(context, await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Ends the one this opened on <paramref name="context" />, if any.</summary>
    internal static void Close(DbContext? context, bool commit) =>
        Close(context, commit, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Ends the one this opened on <paramref name="context" />, if any.</summary>
    internal static async Task Close(DbContext? context, bool commit, CancellationToken cancellationToken)
    {
        if (context is null || !Opened.TryGetValue(context, out var transaction))
        {
            return;
        }

        Opened.Remove(context);

        await using (transaction.ConfigureAwait(false))
        {
            if (commit)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
