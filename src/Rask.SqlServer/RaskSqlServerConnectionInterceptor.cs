using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Rask.SqlServer;

/// <summary>
/// Applies the configured <see cref="SqlServerOptions"/> session settings every time a SQL Server connection is
/// opened. Registered by <c>UseRaskSqlServer(...)</c>.
/// </summary>
/// <remarks>
/// SqlClient resets a pooled connection's session state when it is handed out again, so <c>LOCK_TIMEOUT</c> and
/// <c>XACT_ABORT</c> do not survive a round trip through the pool — they must be applied on <b>every</b> open.
/// The settings are read from the context's <see cref="RaskSqlServerOptionsExtension"/> at open time rather than
/// captured here, so the last <c>UseRaskSqlServer</c> call on a configuration is the one that takes effect.
/// </remarks>
internal sealed class RaskSqlServerConnectionInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is SqlConnection sqlServer && !JoinedATransaction(eventData) && OptionsFor(eventData.Context) is { } options)
        {
            SqlServerSessionSettings.Apply(sqlServer, options);
        }
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is SqlConnection sqlServer && !JoinedATransaction(eventData) && OptionsFor(eventData.Context) is { } options)
        {
            await SqlServerSessionSettings.ApplyAsync(sqlServer, options, cancellationToken).ConfigureAwait(false);
        }
    }

    // A context joining Db.Transaction as it opens is pointed at the transaction's connection instead, and EF still
    // reports the open. That connection got its settings when it opened, and a SET sent now has no transaction on its
    // command, which SqlClient refuses on a connection in a local transaction. A real open never has one: EF clears
    // the context's transactions before opening.
    internal static bool JoinedATransaction(ConnectionEndEventData eventData) =>
        eventData.Context?.Database.CurrentTransaction is not null;

    /// <summary>The settings of the last <c>UseRaskSqlServer</c> call on <paramref name="context"/>'s configuration.</summary>
    internal static SqlServerOptions? OptionsFor(DbContext? context) =>
        context?.GetService<IDbContextOptions>().FindExtension<RaskSqlServerOptionsExtension>()?.Options;
}
