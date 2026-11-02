using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data;

/// <summary>
///     Joins an app's own context to the <c>Db.Transaction</c> in progress before its first command, even one
///     created before the block — so what it runs inside (a LINQ read, <c>SaveChanges</c>, <c>ExecuteSqlRaw</c>,
///     <c>SqlQueryRaw</c>) runs inside the transaction, and a later <c>db:</c> call with it finds it joined already.
/// </summary>
/// <remarks>
///     Both hooks are needed: EF Core creates a non-query's command before it opens the connection, and a query's after.
///     A save joins before it starts too: a save of several commands begins a transaction of its own, which opens the
///     connection first and then refuses one the opening joined — so it must find the context joined already. Being a
///     save interceptor is also what puts it on every context: the <c>ISaveChangesInterceptor</c> services each host
///     already hands its contexts carry it, and EF Core takes each role an interceptor implements. Outside a
///     <c>Db.Transaction</c> it reads one <see cref="AsyncLocal{T}" /> and lets the connection open as it would.
/// </remarks>
internal sealed class AmbientTransactionInterceptor
    : DbConnectionInterceptor, IDbCommandInterceptor, ISaveChangesInterceptor
{
    internal static readonly AmbientTransactionInterceptor Instance = new();

    private AmbientTransactionInterceptor()
    {
    }

    public override InterceptionResult ConnectionOpening(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
        AmbientTransaction.JoinBeforeUse(eventData.Context) ? InterceptionResult.Suppress() : result;

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default) =>
        new(ConnectionOpening(connection, eventData, result));

    InterceptionResult<DbCommand> IDbCommandInterceptor.CommandCreating(
        CommandCorrelatedEventData eventData, InterceptionResult<DbCommand> result)
    {
        AmbientTransaction.JoinBeforeUse(eventData.Context);

        // Migrate and EnsureCreated are refused at their first command, joined or joining just now.
        if (eventData.CommandSource == CommandSource.Migrations)
        {
            AmbientTransaction.RefuseSchemaWork(eventData.Context);
        }

        return result;
    }

    InterceptionResult<int> ISaveChangesInterceptor.SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        AmbientTransaction.JoinBeforeUse(eventData.Context);
        return result;
    }

    ValueTask<InterceptionResult<int>> ISaveChangesInterceptor.SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken)
    {
        AmbientTransaction.JoinBeforeUse(eventData.Context);
        return new(result);
    }
}
