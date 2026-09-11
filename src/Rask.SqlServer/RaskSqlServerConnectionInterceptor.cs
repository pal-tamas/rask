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
        if (connection is SqlConnection sqlServer && OptionsFor(eventData.Context) is { } options)
        {
            SqlServerSessionSettings.Apply(sqlServer, options);
        }
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is SqlConnection sqlServer && OptionsFor(eventData.Context) is { } options)
        {
            await SqlServerSessionSettings.ApplyAsync(sqlServer, options, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The settings of the last <c>UseRaskSqlServer</c> call on <paramref name="context"/>'s configuration.</summary>
    internal static SqlServerOptions? OptionsFor(DbContext? context) =>
        context?.GetService<IDbContextOptions>().FindExtension<RaskSqlServerOptionsExtension>()?.Options;
}
