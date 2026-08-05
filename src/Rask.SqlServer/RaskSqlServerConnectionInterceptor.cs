using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.SqlServer;

/// <summary>
/// An Entity Framework Core connection interceptor that applies the configured
/// <see cref="RaskSqlServerOptions"/> session settings every time a SQL Server connection is opened.
/// Registered for you by
/// <see cref="RaskSqlServerDbContextOptionsExtensions.UseRaskSqlServer(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder, string, Action{RaskSqlServerOptions}?)"/>.
/// </summary>
/// <remarks>
/// SqlClient pools physical connections and resets their session state when one is returned, so
/// <c>LOCK_TIMEOUT</c> and <c>XACT_ABORT</c> do not survive a round trip through the pool — they must be
/// applied on <b>every</b> open, which is what the <see cref="ConnectionOpened"/> hook is for.
/// </remarks>
public sealed class RaskSqlServerConnectionInterceptor : DbConnectionInterceptor
{
    private readonly RaskSqlServerOptions _options;

    /// <summary>Creates an interceptor that applies <paramref name="options"/> on each connection open.</summary>
    public RaskSqlServerConnectionInterceptor(RaskSqlServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc/>
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is SqlConnection sqlServer)
        {
            SqlServerSessionSettings.Apply(sqlServer, _options);
        }
    }

    /// <inheritdoc/>
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is SqlConnection sqlServer)
        {
            await SqlServerSessionSettings.ApplyAsync(sqlServer, _options, cancellationToken).ConfigureAwait(false);
        }
    }
}
