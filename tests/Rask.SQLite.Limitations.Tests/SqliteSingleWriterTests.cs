using Microsoft.Data.Sqlite;

namespace Rask.SQLite.Limitations.Tests;

// Pins the single-writer limitation docs/sqlite.md documents. It is about the database FILE's write lock — the
// SQLITE_BUSY a second writer gets — so, unlike the rest of the limitations, it keeps a file of its own: a database in
// shared-cache memory locks per table and answers SQLITE_LOCKED instead.
public sealed class SqliteSingleWriterTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-sqlite-single-writer-{Guid.NewGuid():N}.db");

    [Fact]
    public void Single_writer_a_second_immediate_transaction_is_busy()
    {
        using var writer = Open();
        Exec(writer, "PRAGMA busy_timeout = 0;");
        Exec(writer, "CREATE TABLE t (x);");
        Exec(writer, "BEGIN IMMEDIATE;"); // takes the write lock and holds it (no commit)
        using var second = Open();
        Exec(second, "PRAGMA busy_timeout = 0;");
        using var begin = second.CreateCommand();
        begin.CommandText = "BEGIN IMMEDIATE;";
        begin.CommandTimeout = 1; // cap the driver's SQLITE_BUSY retry (0 would mean retry forever)

        var ex = Assert.Throws<SqliteException>(() => begin.ExecuteNonQuery());

        Assert.Equal(5, ex.SqliteErrorCode); // SQLITE_BUSY — writes serialize to one writer

        Exec(writer, "ROLLBACK;");
    }

    // Unpooled, so disposing a connection closes the file and Dispose can delete it.
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        connection.Open();
        return connection;
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        foreach (var path in new[] { _dbPath, $"{_dbPath}-journal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
