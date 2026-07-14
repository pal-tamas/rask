using Microsoft.Data.Sqlite;

namespace Rask.SQLite;

/// <summary>
/// Hands out <see cref="SqliteConnection"/>s that apply the configured production pragmas on every
/// open — the raw-ADO.NET counterpart to
/// <see cref="RaskSqliteDbContextOptionsExtensions.UseRaskSqlite(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder, string, Action{SqlitePragmaOptions}?)"/>
/// for code that talks to SQLite without Entity Framework Core. Register it with
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
}
