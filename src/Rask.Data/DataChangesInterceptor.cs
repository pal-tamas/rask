using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
///     Tells the current session's <see cref="IDataChanges" /> which aggregate types a save wrote, once the
///     save is durable.
/// </summary>
/// <remarks>
///     <para>
///         Collected in <c>SavingChanges</c>, because a deleted entity is detached once the save completes.
///         Reported in <c>SavedChanges</c> — or, when the save ran inside a transaction the caller opened, at
///         that transaction's commit, and dropped on its rollback. Reporting before the commit would let a
///         refetch on another connection read the rows as they were, and cache that until the next change.
///     </para>
///     <para>
///         Two sets per context, not one. EF commits the transaction it opens around a plain
///         <c>SaveChanges</c> BEFORE <c>SavedChanges</c> runs, so a commit handler that flushed everything
///         collected would report every ordinary save early and then again. Only a save that found a
///         transaction still open moves its types to the deferred set, and only the deferred set is flushed
///         on commit.
///     </para>
/// </remarks>
internal sealed class DataChangesInterceptor : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private static readonly ConcurrentDictionary<Type, Type> Roots = new();

    private readonly ConditionalWeakTable<DbContext, HashSet<Type>> _collected = new();
    private readonly ConditionalWeakTable<DbContext, HashSet<Type>> _deferred = new();

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

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Remove(_collected, eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Remove(_collected, eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    void IDbTransactionInterceptor.TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        Flush(eventData.Context);

    Task IDbTransactionInterceptor.TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
    {
        Flush(eventData.Context);
        return Task.CompletedTask;
    }

    void IDbTransactionInterceptor.TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        Remove(_deferred, eventData.Context);

    Task IDbTransactionInterceptor.TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
    {
        Remove(_deferred, eventData.Context);
        return Task.CompletedTask;
    }

    void IDbTransactionInterceptor.TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) =>
        Remove(_deferred, eventData.Context);

    Task IDbTransactionInterceptor.TransactionFailedAsync(
        DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken)
    {
        Remove(_deferred, eventData.Context);
        return Task.CompletedTask;
    }

    private void Collect(DbContext? context)
    {
        // Nobody to tell outside a session, so nothing is worth walking the tracker for.
        if (context is null || Db.ScopeServices is null)
        {
            return;
        }

        HashSet<Type>? types = null;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                (types ??= []).Add(RootOf(entry.Metadata));
            }
        }

        if (types is not null)
        {
            _collected.AddOrUpdate(context, types);
        }
    }

    private void Saved(DbContext? context)
    {
        if (context is null || !_collected.TryGetValue(context, out var types))
        {
            return;
        }

        _collected.Remove(context);
        if (context.Database.CurrentTransaction is null)
        {
            Notify(types);
            return;
        }

        // Written, not yet committed: held until the caller's transaction ends one way or the other.
        _deferred.GetValue(context, static _ => []).UnionWith(types);
    }

    private void Flush(DbContext? context)
    {
        if (context is null || !_deferred.TryGetValue(context, out var types))
        {
            return;
        }

        _deferred.Remove(context);
        Notify(types);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The save has committed. An observer that throws must not surface as a failed save, "
                        + "or the caller retries a write that already happened.")]
    private static void Notify(HashSet<Type> types)
    {
        if (Db.ScopeServices is not { } scope)
        {
            return;
        }

        foreach (var observer in scope.GetServices<IDataChanges>())
        {
            try
            {
                observer.Saved(types);
            }
            catch (Exception)
            {
                // See the justification: a stale screen is recoverable, a duplicated write is not.
            }
        }
    }

    private static void Remove(ConditionalWeakTable<DbContext, HashSet<Type>> table, DbContext? context)
    {
        if (context is not null)
        {
            table.Remove(context);
        }
    }

    /// <summary>
    ///     The aggregate a row belongs to, by type: an owned type or a child entity reports its root, found
    ///     by walking foreign keys up to an <see cref="IAggregate" />; anything else reports itself.
    /// </summary>
    /// <remarks>
    ///     By metadata rather than through tracked entries, so a child saved without its root loaded still
    ///     names the root. Cached per type: the answer is a property of the model, not of the save.
    /// </remarks>
    private static Type RootOf(IEntityType entityType) =>
        Roots.GetOrAdd(entityType.ClrType, static (_, type) => FindRoot(type, 0) ?? type.ClrType, entityType);

    private static Type? FindRoot(IReadOnlyEntityType type, int depth)
    {
        // The same ceiling the auditing walk uses: deeper than this is several aggregates under one name.
        if (depth >= 4)
        {
            return null;
        }

        if (typeof(IAggregate).IsAssignableFrom(type.ClrType))
        {
            return type.ClrType;
        }

        if (type.FindOwnership() is { } ownership)
        {
            return FindRoot(ownership.PrincipalEntityType, depth + 1);
        }

        if (!typeof(IEntity).IsAssignableFrom(type.ClrType))
        {
            return null;
        }

        foreach (var foreignKey in type.GetForeignKeys())
        {
            if (typeof(IEntity).IsAssignableFrom(foreignKey.PrincipalEntityType.ClrType)
                && FindRoot(foreignKey.PrincipalEntityType, depth + 1) is { } root)
            {
                return root;
            }
        }

        return null;
    }
}
