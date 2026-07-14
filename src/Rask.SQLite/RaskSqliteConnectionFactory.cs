using System.Data;
using Microsoft.Data.Sqlite;

namespace Rask.SQLite;

/// <summary>
/// Default <see cref="IRaskSqliteConnectionFactory"/>: opens connections for a fixed connection string
/// and applies the configured pragmas whenever a connection transitions to
/// <see cref="ConnectionState.Open"/> — including a pooled connection being reused, which fires the
/// event again and re-applies the per-connection pragmas.
/// </summary>
internal sealed class RaskSqliteConnectionFactory : IRaskSqliteConnectionFactory
{
    private readonly string _connectionString;
    private readonly SqlitePragmaOptions _options;

    public RaskSqliteConnectionFactory(string connectionString, SqlitePragmaOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        ArgumentNullException.ThrowIfNull(options);
        _connectionString = connectionString;
        _options = options;
    }

    public SqliteConnection Create()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.StateChange += OnStateChange;
        return connection;
    }

    public SqliteConnection CreateOpen()
    {
        var connection = Create();
        connection.Open();
        return connection;
    }

    public async Task<SqliteConnection> CreateOpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = Create();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private void OnStateChange(object sender, StateChangeEventArgs e)
    {
        if (e.CurrentState == ConnectionState.Open && sender is SqliteConnection connection)
        {
            SqlitePragmas.Apply(connection, _options);
        }
    }
}
