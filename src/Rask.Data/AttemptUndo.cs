using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     Takes back what a save inside a transaction changed on the objects it wrote, once that transaction rolls back:
///     the aggregate's remembered read, its <c>Version</c>, its "has a row" mark, and a key the database generated for a
///     row that is now gone (<see cref="DomainEventInterceptor" /> gives the domain events back). A retried attempt — or
///     a caller that tries again by hand — then saves them exactly as if the failed attempt had never run, even an
///     aggregate loaded before the block began.
/// </summary>
/// <remarks>
///     <para>
///         Captured as a save begins (<see cref="WritableChildrenInterceptor" /> runs first), before any other
///         interceptor bumps a version or stamps a row. Only the first save of an object in a transaction is captured,
///         so two saves of it in one are taken back to where it stood before the first.
///     </para>
///     <para>
///         Inside <c>Db.Transaction</c> the steps run from its rollback; inside a transaction of the context's own
///         (a <c>BulkInsert</c> in one transaction, <c>Database.BeginTransaction</c>) from EF Core's rolled-back
///         notice, which an explicit rollback sends — not a transaction disposed uncommitted.
///     </para>
/// </remarks>
internal static class AttemptUndo
{
    // Which transaction already holds an object's state from before it: one entry per object, weak on both sides.
    private static readonly ConditionalWeakTable<object, object> Held = new();

    // The steps of transactions of a context's own, run when one rolls back and dropped when it commits.
    private static readonly ConditionalWeakTable<DbTransaction, List<Action>> Own = new();

    /// <summary>Remembers, for the transaction <paramref name="context" /> is in, what its save is about to change.</summary>
    internal static void Capture(DbContext context)
    {
        // Joined here at the latest — AmbientTransactionInterceptor would join it in this same SavingChanges, after us.
        AmbientTransaction.JoinBeforeUse(context);
        object? attempt = AmbientTransaction.Of(context) ?? (object?)context.Database.CurrentTransaction?.GetDbTransaction();
        if (attempt is null)
        {
            return;
        }

        var undo = Steps(context, attempt);
        if (undo.Count == 0)
        {
            return;
        }

        if (attempt is AmbientTransaction ambient)
        {
            ambient.OnRollback(() =>
            {
                Run(undo);
                return Task.CompletedTask;
            });
        }
        else
        {
            Own.GetValue((DbTransaction)attempt, static _ => []).AddRange(undo);
        }
    }

    /// <summary>A transaction of a context's own rolled back: what its saves changed is taken back.</summary>
    internal static void RolledBack(DbTransaction transaction)
    {
        if (Own.TryGetValue(transaction, out var undo))
        {
            Own.Remove(transaction);
            Run(undo);
        }
    }

    /// <summary>A transaction of a context's own committed: what its saves changed stays.</summary>
    internal static void Committed(DbTransaction transaction) => Own.Remove(transaction);

    // What the save about to run would change on each tracked object not already held for this transaction.
    private static List<Action> Steps(DbContext context, object attempt)
    {
        var undo = new List<Action>();
        var tracker = context.ChangeTracker;
        var detecting = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;

        try
        {
            foreach (var entry in tracker.Entries())
            {
                var entity = entry.Entity;
                if (Held.TryGetValue(entity, out var holder) && ReferenceEquals(holder, attempt))
                {
                    continue;
                }

                Held.AddOrUpdate(entity, attempt);

                if (entity is IAttemptState state)
                {
                    var before = state.Before();
                    var read = AggregateSave.ReadOf(entity);
                    undo.Add(() =>
                    {
                        state.Restore(before);
                        AggregateSave.ResetRead(entity, read);
                    });
                }

                if (entry.State == EntityState.Added && entity is IGeneratedKey key &&
                    entry.Metadata.FindPrimaryKey()?.Properties.Any(p => entry.Property(p.Name).IsTemporary) == true)
                {
                    undo.Add(key.ClearKey);
                }
            }
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = detecting;
        }

        return undo;
    }

    private static void Run(List<Action> undo)
    {
        foreach (var step in undo)
        {
            step();
        }
    }
}
