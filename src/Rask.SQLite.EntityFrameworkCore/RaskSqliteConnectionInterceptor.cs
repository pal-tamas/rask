using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.SQLite;

/// <summary>
/// An Entity Framework Core connection interceptor that applies the configured
/// <see cref="SqlitePragmaOptions"/> every time a SQLite connection is opened. Registered for you by
/// <see cref="RaskSqliteDbContextOptionsExtensions.UseRaskSqlite(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder, string, Action{SqlitePragmaOptions}?, Action{SqliteBusyRetryOptions}?)"/>.
/// </summary>
/// <remarks>
/// The per-connection pragmas (<c>foreign_keys</c>, <c>busy_timeout</c>, <c>synchronous</c>, …) do not
/// persist, and EF Core pools connections, so they must run on <b>every</b> open — hence the
/// <see cref="ConnectionOpened"/> / <see cref="ConnectionOpenedAsync"/> hook rather than a one-time setup.
/// The same hook re-registers <see cref="SqliteCollations"/>, which replaces EF Core's culture-sensitive
/// <c>EF_DECIMAL</c> collation with an invariant one so ordering a <see cref="decimal"/> column is correct
/// on every locale; a pooled connection's <c>Deactivate()</c> un-registers it, so it too must run on
/// every open.
/// </remarks>
public sealed class RaskSqliteConnectionInterceptor : DbConnectionInterceptor
{
    private readonly SqlitePragmaOptions _options;

    /// <summary>Creates an interceptor that applies <paramref name="options"/> on each connection open.</summary>
    public RaskSqliteConnectionInterceptor(SqlitePragmaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc/>
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is SqliteConnection sqlite)
        {
            SqlitePragmas.Apply(sqlite, _options);
            SqliteCollations.Apply(sqlite);
        }
    }

    /// <inheritdoc/>
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is SqliteConnection sqlite)
        {
            await SqlitePragmas.ApplyAsync(sqlite, _options, cancellationToken).ConfigureAwait(false);
            SqliteCollations.Apply(sqlite);
        }
    }
}
