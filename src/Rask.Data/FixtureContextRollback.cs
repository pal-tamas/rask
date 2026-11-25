using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     Takes back what a save of a <see cref="TestDatabase" />'s own context wrote inside a transaction, once that
///     transaction rolls back: each row it wrote is tracked again as the database still has it — a new one let go, a
///     changed or deleted one unchanged at the values it had before — so the next save does not send it again.
/// </summary>
/// <remarks>
///     <para>
///         A page's context is gone with its request, and a rolled-back save goes with it; the fixture's context lives
///         for the whole test, so after a save EF Core accepted inside a <c>Db.Transaction</c> that then threw, it would
///         track the rows as written when the database never kept them, and the next save would carry them along — or
///         fail on a version the database never had.
///     </para>
///     <para>
///         Captured as the save begins, last in the chain (<see cref="FixtureContextKeeper" />), so the stamps are what
///         it would write; taken back in reverse, so two saves of one row in one transaction end where it stood before
///         the first. <see cref="AttemptUndo" /> takes back what the save changed on the objects themselves.
///     </para>
/// </remarks>
internal sealed class FixtureContextRollback
{
    // The rows each transaction's saves wrote, by the transaction: a Db.Transaction, or one of the context's own.
    private readonly ConditionalWeakTable<object, List<Snapshot>> _pending = new();

    /// <summary>Remembers, for the transaction <paramref name="owner" /> is in, how each row its save writes stood.</summary>
    internal void Capture(DbContext owner)
    {
        object? attempt = AmbientTransaction.Of(owner) ?? (object?)owner.Database.CurrentTransaction?.GetDbTransaction();
        if (attempt is null)
        {
            return;
        }

        List<Snapshot> written =
        [
            .. owner.ChangeTracker.Entries()
                .Where(static entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(static entry => new Snapshot(entry.Entity, entry.State, entry.OriginalValues.Clone())),
        ];
        if (written.Count == 0)
        {
            return;
        }

        if (_pending.TryGetValue(attempt, out var held))
        {
            held.AddRange(written);
            return;
        }

        _pending.Add(attempt, written);
        if (attempt is AmbientTransaction ambient)
        {
            ambient.OnRollback(() =>
            {
                RolledBack(ambient, owner);
                return Task.CompletedTask;
            });
        }
    }

    /// <summary>A transaction of the context's own rolled back: what its saves wrote is taken back.</summary>
    internal void RolledBack(DbTransaction transaction, DbContext owner) => RolledBack((object)transaction, owner);

    /// <summary>A transaction of the context's own committed: what its saves wrote stays.</summary>
    internal void Committed(DbTransaction transaction) => _pending.Remove(transaction);

    /// <summary>A save of <paramref name="owner" /> failed and it let go of everything: nothing is left to take back.</summary>
    internal void Forget(DbContext owner)
    {
        if ((AmbientTransaction.Of(owner) ?? (object?)owner.Database.CurrentTransaction?.GetDbTransaction()) is { } attempt)
        {
            _pending.Remove(attempt);
        }
    }

    private void RolledBack(object attempt, DbContext owner)
    {
        if (!_pending.TryGetValue(attempt, out var written))
        {
            return;
        }

        _pending.Remove(attempt);
        var tracker = owner.ChangeTracker;
        var detecting = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;
        try
        {
            for (var i = written.Count - 1; i >= 0; i--)
            {
                written[i].Restore(owner);
            }
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = detecting;
        }
    }

    // A row as it stood when a save of the fixture's context began: what it was about to be, and its values in the database.
    // Asked of the context as it is now, never through the entry the save held: one it has let go of since is stale.
    private sealed record Snapshot(object Entity, EntityState State, PropertyValues Stored)
    {
        public void Restore(DbContext owner)
        {
            EntityEntry? entry = null;
            try
            {
                entry = owner.Entry(Entity);
                if (State == EntityState.Added)
                {
                    entry.State = EntityState.Detached;
                    return;
                }

                // A deleted row the save let go of is tracked again: the database still has it.
                entry.State = EntityState.Unchanged;
                entry.CurrentValues.SetValues(Stored);
                entry.OriginalValues.SetValues(Stored);
                entry.State = EntityState.Unchanged;
            }
            catch (InvalidOperationException)
            {
                // A shape the values cannot be copied back onto: let it go, so the next query reads the row the database has.
                entry?.State = EntityState.Detached;
            }
        }
    }
}
