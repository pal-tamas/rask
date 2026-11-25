using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data;

/// <summary>
///     Keeps a <see cref="TestDatabase" />'s own long-lived context (<see cref="TestDatabase.Context" />) as the database has
///     its rows: a row another context of the database saved is the saved row in it too, and a save of its own that failed
///     leaves nothing behind for the next one to carry.
/// </summary>
/// <remarks>
///     <para>
///         A test seeds through the fixture's context and then acts through the model surface (<c>Order.Find</c>,
///         <c>order.Save()</c>), which opens contexts of its own — so the instance the fixture's context still tracks would
///         answer the next query on it with the values from before (EF Core's identity resolution hands back the tracked
///         instance, not the row). When another context's save is durable, each row it wrote that the fixture's context
///         tracks UNCHANGED takes the saved values (current and original alike, so it stays unchanged and its version is
///         the new one); a deleted row is let go. A row the test changed and has not saved is the test's, and is left alone.
///     </para>
///     <para>
///         Purely in memory — no query and no operation on the fixture's context — because a save elsewhere may run while
///         that context is itself saving (a domain-event handler after its commit).
///     </para>
///     <para>
///         Collected in <c>SavingChanges</c> (a deleted entity is detached once the save completes) and applied in
///         <c>SavedChanges</c>, or at the commit of a transaction the save found open, as <see cref="DataChangesInterceptor" />
///         reports; a rollback drops it. A save of its own inside a transaction that then rolls back is taken back
///         (<see cref="FixtureContextRollback" />), and one the database refused lets go of what it tracked.
///     </para>
/// </remarks>
internal sealed class FixtureContextKeeper : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly ConditionalWeakTable<DbContext, List<Written>> _collected = new();
    private readonly ConditionalWeakTable<DbContext, List<Written>> _deferred = new();
    private readonly FixtureContextRollback _rollback = new();

    // The fixture's own context; null while it is not made yet.
    private DbContext? Owner { get; set; }

    /// <summary>Makes <paramref name="owner" /> the fixture's own context, the one kept as the database has it.</summary>
    internal TContext Keep<TContext>(TContext owner)
        where TContext : DbContext
    {
        Owner = owner;
        return owner;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Saved(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Saved(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Failed(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Failed(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    void IDbTransactionInterceptor.TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        Committed(transaction, eventData.Context);

    Task IDbTransactionInterceptor.TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
    {
        Committed(transaction, eventData.Context);
        return Task.CompletedTask;
    }

    void IDbTransactionInterceptor.TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        RolledBack(transaction, eventData.Context);

    Task IDbTransactionInterceptor.TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
    {
        RolledBack(transaction, eventData.Context);
        return Task.CompletedTask;
    }

    private void Collect(DbContext? context)
    {
        if (context is null || Owner is null)
        {
            return;
        }

        if (ReferenceEquals(context, Owner))
        {
            // Its own save, inside a transaction that may still roll back: what it writes is remembered as it stood.
            _rollback.Capture(context);
            return;
        }

        // Last in the chain, so the soft delete and the stamps are already what is written.
        List<Written> written =
        [
            .. context.ChangeTracker.Entries()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && !entry.Metadata.IsOwned())
                .Select(entry => new Written(entry, entry.State == EntityState.Deleted ? RowKey.Of(entry) : null)),
        ];
        _collected.AddOrUpdate(context, written);
    }

    private void Saved(DbContext? context)
    {
        if (context is null || !_collected.TryGetValue(context, out var written))
        {
            return;
        }

        _collected.Remove(context);
        if (context.Database.CurrentTransaction is not null)
        {
            // Not durable until the caller's transaction commits.
            _deferred.GetOrCreateValue(context).AddRange(written);
            return;
        }

        Apply(written);
    }

    private void Committed(DbTransaction transaction, DbContext? context)
    {
        if (ReferenceEquals(context, Owner))
        {
            _rollback.Committed(transaction);
        }

        if (context is not null && _deferred.TryGetValue(context, out var written))
        {
            _deferred.Remove(context);
            Apply(written);
        }
    }

    private void RolledBack(DbTransaction transaction, DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        _deferred.Remove(context);
        if (ReferenceEquals(context, Owner))
        {
            _rollback.RolledBack(transaction, context);
        }
    }

    private void Failed(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        _collected.Remove(context);
        if (ReferenceEquals(context, Owner))
        {
            // As a page's context would be gone with its request: a refused change does not ride along with the next save.
            context.ChangeTracker.Clear();
            _rollback.Forget(context);
        }
    }

    private void Apply(List<Written> written)
    {
        if (Owner is not { } owner || written.Count == 0)
        {
            return;
        }

        var tracked = Tracked(owner);
        if (tracked.Count == 0)
        {
            return;
        }

        foreach (var row in written)
        {
            if (tracked.TryGetValue(row.Key ?? RowKey.Of(row.Entry), out var mine))
            {
                Refresh(mine, row);
            }
        }
    }

    private static void Refresh(EntityEntry mine, Written row)
    {
        // A change the test made that EF Core has not noticed yet is still the test's.
        mine.DetectChanges();
        if (mine.State != EntityState.Unchanged)
        {
            return;
        }

        if (row.Key is not null)
        {
            mine.State = EntityState.Detached;
            return;
        }

        var saved = row.Entry.CurrentValues;
        try
        {
            mine.OriginalValues.SetValues(saved);
            mine.CurrentValues.SetValues(saved);
            mine.State = EntityState.Unchanged;
        }
        catch (InvalidOperationException)
        {
            // A shape the values cannot be copied across (a value EF Core will not set here): let it go, so the next
            // query reads the row the database has.
            mine.State = EntityState.Detached;
        }
    }

    // What the fixture's context tracks, by row — read without detecting changes, which is no business of another save.
    private static Dictionary<RowKey, EntityEntry> Tracked(DbContext owner)
    {
        var tracker = owner.ChangeTracker;
        var detecting = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;
        try
        {
            Dictionary<RowKey, EntityEntry> tracked = [];
            foreach (var entry in tracker.Entries().Where(entry => !entry.Metadata.IsOwned()))
            {
                tracked.TryAdd(RowKey.Of(entry), entry);
            }

            return tracked;
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = detecting;
        }
    }

    // A row another context wrote; its key taken at once when it is deleted, since the entry is let go by the save.
    private sealed record Written(EntityEntry Entry, RowKey? Key);

    private sealed class RowKey : IEquatable<RowKey>
    {
        private readonly string _type;
        private readonly object?[] _values;

        private RowKey(string type, object?[] values) => (_type, _values) = (type, values);

        public static RowKey Of(EntityEntry entry)
        {
            var key = entry.Metadata.FindPrimaryKey();
            object?[] values = key is null ? [entry.Entity] : [.. key.Properties.Select(p => entry.Property(p.Name).CurrentValue)];
            return new RowKey(entry.Metadata.Name, values);
        }

        public bool Equals(RowKey? other) =>
            other is not null && string.Equals(_type, other._type, StringComparison.Ordinal) && _values.SequenceEqual(other._values);

        public override bool Equals(object? obj) => Equals(obj as RowKey);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_type, StringComparer.Ordinal);
            foreach (var value in _values)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }
    }
}
