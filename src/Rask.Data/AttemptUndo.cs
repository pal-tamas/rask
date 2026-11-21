using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     Takes back what a save inside a <c>Db.Transaction</c> changed on the objects it wrote, once that attempt rolls
///     back: the aggregate's remembered read, its <c>Version</c>, its "has a row" mark, the domain events the save took
///     off it, and a key the database generated for a row that is now gone. A retried attempt — or a caller that tries
///     again by hand — then saves them exactly as if the failed attempt had never run, even an aggregate loaded before
///     the block began.
/// </summary>
/// <remarks>
///     Captured as a save begins (<see cref="WritableChildrenInterceptor" /> runs first), before any other interceptor
///     bumps a version or stamps a row. Only the first save of an object in an attempt is captured, so two saves of it
///     in one attempt are taken back to where it stood before the first.
/// </remarks>
internal static class AttemptUndo
{
    // Which attempt already holds an object's state from before it: one entry per object, weak on both sides.
    private static readonly ConditionalWeakTable<object, AmbientTransaction> Held = new();

    /// <summary>Remembers, for the attempt <paramref name="context" /> has joined, what its save is about to change.</summary>
    internal static void Capture(DbContext context)
    {
        // Joined here at the latest — AmbientTransactionInterceptor would join it in this same SavingChanges, after us.
        AmbientTransaction.JoinBeforeUse(context);
        if (AmbientTransaction.Of(context) is not { } attempt)
        {
            return;
        }

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

        if (undo.Count > 0)
        {
            attempt.OnRollback(() =>
            {
                foreach (var step in undo)
                {
                    step();
                }

                return Task.CompletedTask;
            });
        }
    }
}
