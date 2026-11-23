using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data;

/// <summary>
///     Hands every entity a query materializes, and every entity a save is about to write, collections of children
///     EF Core can fill — see <see cref="WritableChildren" /> — and marks each aggregate among them as having a row
///     (<see cref="StoredAggregates" />). While one is materialized, the value objects built for it are loaded
///     without their rules (<see cref="Loading" />).
/// </summary>
/// <remarks>
///     Registered first by <see cref="RaskDataServiceCollectionExtensions.AddRaskData(Microsoft.Extensions.DependencyInjection.IServiceCollection)" />,
///     so a refill the other interceptors match is already writable, and added by <see cref="RaskDbContext" /> to a
///     context configured without it.
/// </remarks>
internal sealed class WritableChildrenInterceptor : SaveChangesInterceptor, IMaterializationInterceptor, IDbTransactionInterceptor
{
    /// <summary>The one instance: a materialization interceptor is part of EF Core's service-provider cache key.</summary>
    internal static readonly WritableChildrenInterceptor Instance = new();

    private WritableChildrenInterceptor() => FailedMaterializations.Watch();

    /// <inheritdoc />
    /// <remarks>
    ///     The value objects EF Core builds for the entity — its complex values, between this call and
    ///     <see cref="InitializedInstance" /> — are loaded, not made: a row stored before a rule was tightened still
    ///     loads (<see cref="Loading" />).
    /// </remarks>
    public InterceptionResult<object> CreatingInstance(MaterializationInterceptionData materializationData, InterceptionResult<object> result)
    {
        Loading.Enter();
        return result;
    }

    /// <inheritdoc />
    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        Loading.Leave();
        WritableChildren.Ensure(materializationData.EntityType, entity);
        StoredAggregates.Mark(entity);
        return entity;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Ensure(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Ensure(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Remember(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Remember(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    void IDbTransactionInterceptor.TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        AttemptUndo.Committed(transaction);

    /// <inheritdoc />
    Task IDbTransactionInterceptor.TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
    {
        AttemptUndo.Committed(transaction);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    void IDbTransactionInterceptor.TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        AttemptUndo.RolledBack(transaction);

    /// <inheritdoc />
    Task IDbTransactionInterceptor.TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
    {
        AttemptUndo.RolledBack(transaction);
        return Task.CompletedTask;
    }

    // What the save wrote, for each aggregate staged with Save(): its next save then reads nothing.
    private static void Remember(DbContext? context)
    {
        if (context is not null)
        {
            AggregateSave.RememberSaved(context);
        }
    }

    private static void Ensure(DbContext? context)
    {
        if (context is not null)
        {
            WritableChildren.Ensure(context);
            AttemptUndo.Capture(context);          // before MarkTracked and before any interceptor bumps a version
            StoredAggregates.MarkTracked(context);
        }
    }
}
