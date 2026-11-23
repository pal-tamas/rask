using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
/// An Entity Framework Core connection interceptor that applies the configured
/// <see cref="SqliteOptions"/> every time a SQLite connection is opened. Registered for you by
/// <see cref="RaskSqliteDbContextOptionsExtensions.UseRaskSqlite(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder, IServiceProvider, Action{SqliteOptions}?)"/>.
/// </summary>
/// <remarks>
/// The per-connection pragmas (<c>foreign_keys</c>, <c>busy_timeout</c>, <c>synchronous</c>, …) do not
/// persist, and EF Core pools connections, so they must run on <b>every</b> open — hence the
/// <see cref="ConnectionOpened"/> / <see cref="ConnectionOpenedAsync"/> hook rather than a one-time setup.
/// The same hook re-registers <see cref="SqliteCollations"/>, which replaces EF Core's culture-sensitive
/// <c>EF_DECIMAL</c> collation with an invariant one so ordering a <see cref="decimal"/> column is correct
/// on every locale; a pooled connection's <c>Deactivate()</c> un-registers it, so it too must run on
/// every open. A model that names how text compares (<c>c.OrderTextBy("Hungarian_100_CI_AS")</c>) gets
/// <see cref="SqliteCollations.TextOrder"/> registered beside it, with <c>instr</c> and <c>LIKE</c> compared the same way.
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
        if (connection is SqliteConnection sqlite && !JoinedATransaction(eventData))
        {
            SqlitePragmas.Apply(sqlite, _options);
            ApplyCollations(sqlite, eventData);
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
        if (connection is SqliteConnection sqlite && !JoinedATransaction(eventData))
        {
            await SqlitePragmas.ApplyAsync(sqlite, _options, cancellationToken).ConfigureAwait(false);
            ApplyCollations(sqlite, eventData);
        }
    }

    // The decimal order always; the text order the context's model names with OrderTextBy, when it names one — its text
    // columns were created with it, so a connection without it could not compare them.
    private static void ApplyCollations(SqliteConnection connection, ConnectionEndEventData eventData)
    {
        SqliteCollations.Apply(connection);
        if (eventData.Context?.Model.FindAnnotation(TextCollation.Annotation)?.Value is string name)
        {
            var collation = TextCollation.Parse(name);
            SqliteTextOrder.Apply(connection, collation.Culture.CompareInfo, collation.Options);
        }
    }

    // A context joining Db.Transaction as it opens is pointed at the transaction's connection instead, and EF still
    // reports the open. That connection got its pragmas when it opened, and journal_mode or synchronous may not
    // change inside a transaction. A real open never has one: EF clears the context's transactions before opening.
    private static bool JoinedATransaction(ConnectionEndEventData eventData) =>
        eventData.Context?.Database.CurrentTransaction is not null;
}
