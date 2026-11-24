using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data;

/// <summary>
///     Hands every entity a query materializes, and every entity a save is about to write, collections of children
///     EF Core can fill — see <see cref="WritableChildren" /> — and marks each aggregate among them as having a row
///     (<see cref="StoredAggregates" />), then asks the domain's rules of the save (<see cref="DomainValidation" />), its declared locks included — whose
///     update-locked read it writes in the database's words (<see cref="ForUpdateSql" />) and whose transaction it ends
///     with the save (<see cref="SaveTransaction" />). While one is materialized, the value objects built for it are loaded
///     without their rules (<see cref="RowLoading" />).
/// </summary>
/// <remarks>
///     Registered first by <see cref="RaskDataServiceCollectionExtensions.AddRaskData(Microsoft.Extensions.DependencyInjection.IServiceCollection)" />,
///     so a refill the other interceptors match is already writable, and added by <see cref="RaskDbContext" /> to a
///     context configured without it.
/// </remarks>
internal sealed class WritableChildrenInterceptor
    : SaveChangesInterceptor, IMaterializationInterceptor, IDbTransactionInterceptor, IDbCommandInterceptor
{
    /// <summary>The one instance: a materialization interceptor is part of EF Core's service-provider cache key.</summary>
    internal static readonly WritableChildrenInterceptor Instance = new();

    private WritableChildrenInterceptor() => FailedMaterializations.Watch();

    /// <inheritdoc />
    /// <remarks>
    ///     The value objects EF Core builds for the entity — its complex values, between this call and
    ///     <see cref="InitializedInstance" /> — are loaded, not made: a row stored before a rule was tightened still
    ///     loads (<see cref="RowLoading" />).
    /// </remarks>
    public InterceptionResult<object> CreatingInstance(MaterializationInterceptionData materializationData, InterceptionResult<object> result)
    {
        RowLoading.Enter();
        return result;
    }

    /// <inheritdoc />
    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        RowLoading.Leave();
        WritableChildren.Ensure(materializationData.EntityType, entity);
        ChildPositions.Read(materializationData, entity);
        StoredAggregates.Mark(entity);
        return entity;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Then asks the domain's rules (<see cref="DomainValidation" />): first of every interceptor, so a refused save
    ///     has had nothing rewritten, stamped or versioned.
    /// </remarks>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Ensure(eventData.Context);

        // The same work as the async path. A synchronous save has no other way to await a rule.
        if (eventData.Context is { } context)
        {
            CheckAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        }

        return result;
    }

    /// <inheritdoc />
    /// <remarks>Then asks the domain's rules, as <see cref="SavingChanges" /> does.</remarks>
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Ensure(eventData.Context);
        if (eventData.Context is { } context)
        {
            await CheckAsync(context, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc />
    /// <remarks>Runs what the save left for before its commit (<see cref="SaveFollowUps" />), then commits.</remarks>
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Remember(eventData.Context);
        FollowUp(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        SaveTransaction.Close(eventData.Context, commit: true);
        return result;
    }

    /// <inheritdoc />
    /// <remarks>Runs what the save left for before its commit (<see cref="SaveFollowUps" />), then commits.</remarks>
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Remember(eventData.Context);
        await FollowUp(eventData.Context, cancellationToken).ConfigureAwait(false);
        await SaveTransaction.Close(eventData.Context, commit: true, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    /// <remarks>Rolls back the transaction the domain step opened for the save (<see cref="SaveTransaction" />).</remarks>
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        SaveFollowUps.Forget(eventData.Context);
        SaveTransaction.Close(eventData.Context, commit: false);
    }

    /// <inheritdoc />
    /// <remarks>Rolls back the transaction the domain step opened for the save (<see cref="SaveTransaction" />).</remarks>
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        SaveFollowUps.Forget(eventData.Context);
        return SaveTransaction.Close(eventData.Context, commit: false, CancellationToken.None);
    }

    /// <inheritdoc />
    /// <remarks>Rolls back the transaction the domain step opened for the save (<see cref="SaveTransaction" />).</remarks>
    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        SaveFollowUps.Forget(eventData.Context);
        SaveTransaction.Close(eventData.Context, commit: false);
    }

    /// <inheritdoc />
    /// <remarks>Rolls back the transaction the domain step opened for the save (<see cref="SaveTransaction" />).</remarks>
    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        SaveFollowUps.Forget(eventData.Context);
        return SaveTransaction.Close(eventData.Context, commit: false, CancellationToken.None);
    }

    /// <inheritdoc />
    /// <remarks>A parent read under an update lock is written in the database's own words here (<see cref="ForUpdateSql" />).</remarks>
    InterceptionResult<DbDataReader> IDbCommandInterceptor.ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        ForUpdateSql.Apply(command, eventData.Context);
        return result;
    }

    /// <inheritdoc />
    /// <remarks>As <see cref="IDbCommandInterceptor.ReaderExecuting" />.</remarks>
    ValueTask<InterceptionResult<DbDataReader>> IDbCommandInterceptor.ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken)
    {
        ForUpdateSql.Apply(command, eventData.Context);
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

    // What fails after the rows went out rolls back the transaction the domain step opened for the save, as a failed
    // save does: the follow-up is part of the save.
    private static async Task FollowUp(DbContext? context, CancellationToken cancellationToken)
    {
        try
        {
            await SaveFollowUps.Run(context, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await SaveTransaction.Close(context, commit: false, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    // A refused save never starts, so nothing later rolls back what the domain step opened for it.
    private static async Task CheckAsync(DbContext context, CancellationToken cancellationToken)
    {
        try
        {
            await DomainValidation.CheckAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await SaveTransaction.Close(context, commit: false, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static void Ensure(DbContext? context)
    {
        if (context is not null)
        {
            // A save refused before it went out left its follow-ups behind: they are not this save's.
            SaveFollowUps.Forget(context);
            WritableChildren.Ensure(context);
            ChildPositions.Stamp(context);
            AttemptUndo.Capture(context);          // before MarkTracked and before any interceptor bumps a version
            StoredAggregates.MarkTracked(context);
        }
    }
}
