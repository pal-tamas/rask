using Microsoft.Data.Sqlite;

namespace Rask.SQLite;

/// <summary>
/// Hands out <see cref="SqliteConnection"/>s that apply the configured production pragmas on every
/// open — the raw-ADO.NET counterpart to <c>UseRaskSqlite</c> (in the <c>Rask.SQLite.EntityFrameworkCore</c>
/// package) for code that talks to SQLite without Entity Framework Core. Register it with
/// <see cref="SqliteServiceCollectionExtensions.AddRaskSqlite"/>.
/// </summary>
public interface IRaskSqliteConnectionFactory
{
    /// <summary>
    /// Creates a <b>closed</b> connection with the pragmas pre-wired to apply as soon as it opens (via
    /// its <see cref="System.Data.Common.DbConnection.StateChange"/> event, so pooled reopens re-apply
    /// the per-connection pragmas). Open it yourself with <c>Open()</c>/<c>OpenAsync()</c>.
    /// </summary>
    SqliteConnection Create();

    /// <summary>Creates a connection, opens it (pragmas applied), and returns it ready to use.</summary>
    SqliteConnection CreateOpen();

    /// <summary>Asynchronously creates and opens a connection (pragmas applied) and returns it.</summary>
    Task<SqliteConnection> CreateOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a connection and runs <paramref name="work"/> inside a <c>BEGIN IMMEDIATE</c> transaction,
    /// acquiring the write lock through the <b>non-blocking, fair-interval</b> retry configured on this
    /// factory (Rails-style: a constant poll that yields the thread while it waits). Commits when
    /// <paramref name="work"/> returns; rolls back if it throws; disposes the connection either way. This
    /// is the recommended write path under concurrency. Issue statements with
    /// <see cref="SqliteConnection.CreateCommand"/> inside <paramref name="work"/>.
    /// </summary>
    Task<T> ExecuteInImmediateTransactionAsync<T>(
        Func<SqliteConnection, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);

    /// <summary>The result-less overload of <see cref="ExecuteInImmediateTransactionAsync{T}"/>.</summary>
    Task ExecuteInImmediateTransactionAsync(
        Func<SqliteConnection, CancellationToken, Task> work,
        CancellationToken cancellationToken = default);
}
