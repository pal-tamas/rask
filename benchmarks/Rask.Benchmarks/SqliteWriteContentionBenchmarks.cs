using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Rask.SQLite;

namespace Rask.Benchmarks;

// SQLite is set to become the heart of the framework, so the write path under contention is a hot path
// worth guarding. This measures a burst of N concurrent writers against one WAL database two ways:
//
//   NativeBusyTimeout  — the classic path: BEGIN IMMEDIATE with busy_timeout=5000, so a contended writer
//                        BLOCKS its thread inside Microsoft.Data.Sqlite (Thread.Sleep) until the lock frees.
//   NonBlockingRetry   — Rask's ExecuteInImmediateTransactionAsync: the write lock is taken through the raw
//                        sqlite3 handle with the busy handler off, and a contended writer AWAITS a 1 ms fair
//                        interval (Rails' busy handler, ported) — the thread is freed while it waits.
//
// Both do identical SQL; the difference is who holds the thread while waiting. Watch Allocated and, at
// higher writer counts, how the non-blocking path keeps the thread pool free.
[MemoryDiagnoser]
public class SqliteWriteContentionBenchmarks
{
    private string _dbPath = null!;
    private string _connectionString = null!;
    private readonly SqliteBusyRetryOptions _retry = new();

    [Params(1, 8, 32)]
    public int Writers { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"rask-sqlite-bench-{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        Exec(connection, "PRAGMA journal_mode=WAL;");
        Exec(connection, "PRAGMA synchronous=NORMAL;");
        Exec(connection, "CREATE TABLE writes(id INTEGER PRIMARY KEY, worker INTEGER NOT NULL);");
    }

    [GlobalCleanup]
    public void Cleanup()
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

    [Benchmark(Baseline = true)]
    public async Task NativeBusyTimeout()
    {
        var tasks = Enumerable.Range(0, Writers).Select(worker => Task.Run(async () =>
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            Exec(connection, "PRAGMA busy_timeout=5000;");

            using var transaction = connection.BeginImmediate(); // waits by blocking the thread
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO writes(worker) VALUES ($worker);";
            command.Parameters.AddWithValue("$worker", worker);
            await command.ExecuteNonQueryAsync();
            transaction.Commit();
        }));

        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task NonBlockingRetry()
    {
        var tasks = Enumerable.Range(0, Writers).Select(worker => Task.Run(async () =>
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await connection.ExecuteInImmediateTransactionAsync(_retry, async (c, ct) =>
            {
                await using var command = c.CreateCommand();
                command.CommandText = "INSERT INTO writes(worker) VALUES ($worker);";
                command.Parameters.AddWithValue("$worker", worker);
                await command.ExecuteNonQueryAsync(ct);
            });
        }));

        await Task.WhenAll(tasks);
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
