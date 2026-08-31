using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.SQLite;

/// <summary>
/// An Entity Framework Core connection interceptor that applies the configured
/// <see cref="SqliteOptions"/> every time a SQLite connection is opened. Registered for you by
/// <see cref="RaskSqliteDbContextOptionsExtensions.UseRaskSqlite(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder, string, Action{SqliteOptions}?)"/>.
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
    private readonly SqliteOptions _options;

    /// <summary>Creates an interceptor that applies <paramref name="options"/> on each connection open.</summary>
    public RaskSqliteConnectionInterceptor(SqliteOptions options)
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

    /// <summary>
    /// Runs <see cref="SqlitePragmas.Optimize"/> before the connection closes, so the query planner's
    /// statistics stay current as the data shifts underneath it.
    /// </summary>
    /// <remarks>
    /// This is the moment SQLite's own guidance names for it, and the connection is still open here —
    /// by <c>ConnectionClosed</c> it is not. It is best-effort and bounded by
    /// <see cref="SqliteOptions.AnalysisLimit"/>; a pooled connection reaches this on every return
    /// to the pool, and analyses nothing when nothing has changed.
    /// </remarks>
    public override InterceptionResult ConnectionClosing(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        if (connection is SqliteConnection sqlite && _options.AnalysisLimit is not null)
        {
            SqlitePragmas.Optimize(sqlite);
        }

        return base.ConnectionClosing(connection, eventData, result);
    }

    /// <inheritdoc cref="ConnectionClosing"/>
    public override ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        if (connection is SqliteConnection sqlite && _options.AnalysisLimit is not null)
        {
            SqlitePragmas.Optimize(sqlite);
        }

        return base.ConnectionClosingAsync(connection, eventData, result);
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
