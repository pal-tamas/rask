using Microsoft.Data.Sqlite;

namespace Rask.Benchmarks.Sqlite.Db;

/// <summary>
/// One arm's private database file. Every arm gets its own path, which is what gives it its own
/// Microsoft.Data.Sqlite connection pool (the pool is keyed by connection string) — so an arm can never
/// inherit another's pooled connections, nor the <c>busy_timeout=0</c> that
/// <c>ExecuteInImmediateTransactionAsync</c> leaves on a hand-built connection it has used.
/// </summary>
internal sealed class LoadDb(string label)
{
    internal string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rask-load-{label}-{Guid.NewGuid():N}.db");

    internal string ConnectionString => $"Data Source={Path}";

    /// <summary>Creates the file and applies <paramref name="schema"/>, in the given journal mode.</summary>
    internal void Create(string schema, string journalMode = "WAL")
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        Exec(connection, $"PRAGMA journal_mode={journalMode};");
        Exec(connection, "PRAGMA synchronous=NORMAL;");
        Exec(connection, schema);
    }

    internal static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    internal long Scalar(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    /// <summary>
    /// Clears the pool, then deletes the database and its sidecars. The pool must be cleared first —
    /// pooled handles hold the file open. This runs even on Ctrl-C, or a cancelled soak would strand a
    /// multi-gigabyte WAL in the temp directory.
    /// </summary>
    internal void Delete()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { Path, $"{Path}-shm", $"{Path}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
