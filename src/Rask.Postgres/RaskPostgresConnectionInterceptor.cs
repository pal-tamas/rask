using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Rask.Postgres;

/// <summary>
/// An Entity Framework Core connection interceptor that applies the configured
/// <see cref="RaskPostgresOptions"/> session settings every time a PostgreSQL connection is opened.
/// Registered for you by
/// <see cref="RaskPostgresDbContextOptionsExtensions.UseRaskPostgres(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder, string, Action{RaskPostgresOptions}?)"/>.
/// </summary>
/// <remarks>
/// Npgsql pools physical connections and resets their session state when one is returned, so
/// <c>statement_timeout</c> and friends do not survive a round trip through the pool — they must be applied
/// on <b>every</b> open, which is what the <see cref="ConnectionOpened"/> hook is for.
/// </remarks>
public sealed class RaskPostgresConnectionInterceptor : DbConnectionInterceptor
{
    private readonly RaskPostgresOptions _options;

    /// <summary>Creates an interceptor that applies <paramref name="options"/> on each connection open.</summary>
    public RaskPostgresConnectionInterceptor(RaskPostgresOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc/>
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is NpgsqlConnection postgres)
        {
            PostgresSessionSettings.Apply(postgres, _options);
        }
    }

    /// <inheritdoc/>
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is NpgsqlConnection postgres)
        {
            await PostgresSessionSettings.ApplyAsync(postgres, _options, cancellationToken).ConfigureAwait(false);
        }
    }
}
