using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Transaction = System.Transactions.Transaction;

namespace Rask.Data;

/// <summary>
///     The transaction manager a Rask context's <c>Database</c> asks, in front of EF Core's own: it refuses
///     <c>BeginTransaction</c> on a context inside <c>Db.Transaction</c>, saying why, before EF Core refuses it with
///     "The connection is already in a transaction". Everything else goes straight through.
/// </summary>
/// <remarks>
///     The seam has to be here: EF Core checks for a transaction in progress before it tells any interceptor one is
///     starting, and on a joined context the connection is open already, so nothing is opened either.
/// </remarks>
internal sealed class JoinedTransactionManager(IRelationalConnection connection, DbContext context)
    : IRelationalTransactionManager, ITransactionEnlistmentManager
{
    public IDbContextTransaction? CurrentTransaction => connection.CurrentTransaction;

    public Transaction? EnlistedTransaction => Enlistment.EnlistedTransaction;

    // EF Core's relational connection is its enlistment manager too, though its interface does not say so.
    private ITransactionEnlistmentManager Enlistment => (ITransactionEnlistmentManager)connection;

    public IDbContextTransaction BeginTransaction()
    {
        AmbientTransaction.RefuseOwnTransaction(context);
        return connection.BeginTransaction();
    }

    public IDbContextTransaction BeginTransaction(IsolationLevel isolationLevel)
    {
        AmbientTransaction.RefuseOwnTransaction(context);
        return connection.BeginTransaction(isolationLevel);
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        AmbientTransaction.RefuseOwnTransaction(context);
        return connection.BeginTransactionAsync(cancellationToken);
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
    {
        AmbientTransaction.RefuseOwnTransaction(context);
        return connection.BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    public IDbContextTransaction? UseTransaction(DbTransaction? transaction) => connection.UseTransaction(transaction);

    public IDbContextTransaction? UseTransaction(DbTransaction? transaction, Guid transactionId) =>
        connection.UseTransaction(transaction, transactionId);

    public Task<IDbContextTransaction?> UseTransactionAsync(
        DbTransaction? transaction, CancellationToken cancellationToken = default) =>
        connection.UseTransactionAsync(transaction, cancellationToken);

    public Task<IDbContextTransaction?> UseTransactionAsync(
        DbTransaction? transaction, Guid transactionId, CancellationToken cancellationToken = default) =>
        connection.UseTransactionAsync(transaction, transactionId, cancellationToken);

    public void CommitTransaction() => connection.CommitTransaction();

    public Task CommitTransactionAsync(CancellationToken cancellationToken = default) =>
        connection.CommitTransactionAsync(cancellationToken);

    public void RollbackTransaction() => connection.RollbackTransaction();

    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) =>
        connection.RollbackTransactionAsync(cancellationToken);

    public void EnlistTransaction(Transaction? transaction) => Enlistment.EnlistTransaction(transaction);

    public void ResetState() => connection.ResetState();

    public Task ResetStateAsync(CancellationToken cancellationToken = default) =>
        connection.ResetStateAsync(cancellationToken);
}
