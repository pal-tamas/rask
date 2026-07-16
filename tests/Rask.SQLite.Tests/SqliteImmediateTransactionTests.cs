using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Rask.SQLite.Tests;

// Integration tests for the IMMEDIATE-transaction + non-blocking fair-interval retry, against a real
// temp-file WAL database. They prove the write lock is taken up front, that a contended lock is waited
// out without blocking, and that a lock that never frees times out with SQLITE_BUSY.
public sealed class SqliteImmediateTransactionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-sqlite-imm-{Guid.NewGuid():N}.db");
    private readonly string _connectionString;

    public SqliteImmediateTransactionTests()
    {
        _connectionString = $"Data Source={_dbPath}";
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        Exec(connection, "PRAGMA journal_mode=WAL;");
        Exec(connection, "CREATE TABLE t(id INTEGER PRIMARY KEY, v TEXT NOT NULL);");
    }

    [Fact]
    public async Task Commits_the_work()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await connection.ExecuteInImmediateTransactionAsync(
            new SqliteBusyRetryOptions(),
            async (c, ct) =>
            {
                await using var cmd = c.CreateCommand();
                cmd.CommandText = "INSERT INTO t(v) VALUES('committed');";
                await cmd.ExecuteNonQueryAsync(ct);
            });

        Assert.Equal(1, Count());
    }

    [Fact]
    public async Task Rolls_back_when_work_throws()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.ExecuteInImmediateTransactionAsync(
                new SqliteBusyRetryOptions(),
                async (c, ct) =>
                {
                    await using var cmd = c.CreateCommand();
                    cmd.CommandText = "INSERT INTO t(v) VALUES('doomed');";
                    await cmd.ExecuteNonQueryAsync(ct);
                    throw new InvalidOperationException("boom");
                }));

        Assert.Equal(0, Count());
    }

    [Fact]
    public async Task Retries_and_succeeds_when_the_lock_is_released()
    {
        // A holds the write lock, released after ~50 ms. The waiter must poll (thread-free) and then commit.
        await using var holder = new SqliteConnection(_connectionString);
        await holder.OpenAsync();
        var holderTx = holder.BeginImmediate();
        Insert(holder, holderTx, "holder");

        var release = Task.Run(async () =>
        {
            await Task.Delay(50);
            holderTx.Commit();
        });

        await using var waiter = new SqliteConnection(_connectionString);
        await waiter.OpenAsync();

        await waiter.ExecuteInImmediateTransactionAsync(
            new SqliteBusyRetryOptions { Timeout = TimeSpan.FromSeconds(5), PollInterval = TimeSpan.FromMilliseconds(1) },
            async (c, ct) =>
            {
                await using var cmd = c.CreateCommand();
                cmd.CommandText = "INSERT INTO t(v) VALUES('waiter');";
                await cmd.ExecuteNonQueryAsync(ct);
            });

        await release;
        Assert.Equal(2, Count());
    }

    [Fact]
    public async Task BeginImmediate_holds_the_lock_and_a_contended_wait_times_out_without_blocking()
    {
        // BEGIN IMMEDIATE takes the write lock up front, before any write — a deferred BEGIN would not.
        await using var holder = new SqliteConnection(_connectionString);
        await holder.OpenAsync();
        using var holderTx = holder.BeginImmediate();

        await using var waiter = new SqliteConnection(_connectionString);
        await waiter.OpenAsync();

        var stopwatch = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            waiter.ExecuteInImmediateTransactionAsync(
                new SqliteBusyRetryOptions { Timeout = TimeSpan.FromMilliseconds(150), PollInterval = TimeSpan.FromMilliseconds(1) },
                (_, _) => Task.CompletedTask));
        stopwatch.Stop();

        Assert.Equal(5, exception.SqliteErrorCode); // SQLITE_BUSY
        // The wait ended near the 150 ms timeout — not the driver's multi-second synchronous block.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"took {stopwatch.ElapsedMilliseconds} ms");
        Assert.Equal(0, Count());
    }

    [Fact]
    public async Task Recovers_when_the_handle_arrives_with_a_leaked_transaction()
    {
        // The raw path drives BEGIN/COMMIT through the native handle, invisible to ADO's transaction
        // bookkeeping, and the handle is pooled. Simulate an earlier lease that left a transaction open:
        // once autocommit is off, a plain BEGIN IMMEDIATE would fail non-retryably ("cannot start a
        // transaction within a transaction"). The entry guard must clear it and still commit the work.
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        Exec(connection, "BEGIN IMMEDIATE;"); // leak a write transaction onto the raw handle
        Assert.Equal(0, raw.sqlite3_get_autocommit(connection.Handle!)); // precondition: mid-transaction

        await connection.ExecuteInImmediateTransactionAsync(
            new SqliteBusyRetryOptions(),
            async (c, ct) =>
            {
                await using var cmd = c.CreateCommand();
                cmd.CommandText = "INSERT INTO t(v) VALUES('recovered');";
                await cmd.ExecuteNonQueryAsync(ct);
            });

        Assert.Equal(1, Count());
        Assert.Equal(1, raw.sqlite3_get_autocommit(connection.Handle!)); // handle left clean
    }

    [Fact]
    public async Task Leaves_the_handle_in_autocommit_after_work_throws()
    {
        // The finally guard must never return a mid-transaction handle to the pool, even when work throws.
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.ExecuteInImmediateTransactionAsync(
                new SqliteBusyRetryOptions(),
                async (c, ct) =>
                {
                    await using var cmd = c.CreateCommand();
                    cmd.CommandText = "INSERT INTO t(v) VALUES('doomed');";
                    await cmd.ExecuteNonQueryAsync(ct);
                    throw new InvalidOperationException("boom");
                }));

        Assert.Equal(0, Count());
        Assert.Equal(1, raw.sqlite3_get_autocommit(connection.Handle!)); // rolled back, pool not poisoned
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, string value)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO t(v) VALUES($v);";
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private long Count()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM t;";
        return (long)cmd.ExecuteScalar()!;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-shm", $"{_dbPath}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
