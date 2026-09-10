using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.SQLite.Tests;

// Stress tests for the write path that will sit at the heart of the framework: hammer a single SQLite
// file with far more concurrent writers than there are threads, and prove every one commits — no
// "database is locked", no thread-pool starvation. If the wait were thread-blocking (the native
// busy_timeout / driver path), hundreds of writers would each pin a thread and the pool would collapse;
// the fair-interval retry frees the thread between polls, so it scales.
public sealed class SqliteConcurrencyStressTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-sqlite-stress-{Guid.NewGuid():N}.db");

    // POOLING OFF, for every connection this class opens — and it is the flake fix, not a tuning knob.
    //
    // Microsoft.Data.Sqlite's connection pool is process-global, and so is ClearAllPools(): it disposes the
    // underlying sqlite3 handle of connections that are currently LEASED AND IN USE, not just idle ones
    // (verified against plain MDS, so not a Rask defect). Classes across the SQLite test assemblies call it
    // in Dispose to release a temp-file handle, and vstest batches compatible assemblies into ONE testhost
    // process — so `[assembly: CollectionBehavior(DisableTestParallelization)]`, which is per-assembly,
    // cannot keep a SIBLING ASSEMBLY's teardown away from this class's live connections. That is exactly
    // why the 500-writer burst fails only in a solution-wide run and never alone or per-assembly: a sibling
    // clears the pool mid-burst, and `connection.Handle` then hands a disposed SafeHandle to the next raw
    // call (`sqlite3_busy_timeout`, before any guard) — surfacing as ObjectDisposedException, or as a bare
    // SQLITE_ERROR whose errmsg reads "not an error".
    //
    // A connection that was never pooled cannot be disposed by ClearAllPools, so this closes the family
    // rather than narrowing the window. It does not weaken what the test proves: every writer still opens,
    // contends for the single write lock, and commits through the same fair-interval retry — each now on its
    // own sqlite3 handle, which is strictly more contention and removes the pool as a confound.
    private const string PoolingOff = ";Pooling=False";

    private readonly ServiceProvider _provider;
    private readonly ISqlite _factory;

    public SqliteConcurrencyStressTests()
    {
        using (var connection = new SqliteConnection($"Data Source={_dbPath}{PoolingOff}"))
        {
            connection.Open();
            Exec(connection, "PRAGMA journal_mode=WAL;");
            Exec(connection, "CREATE TABLE writes(id INTEGER PRIMARY KEY, worker INTEGER NOT NULL);");
        }

        var services = new ServiceCollection();
        // A generous per-writer timeout so a busy CI box under heavy contention never spuriously times out.
        services.AddRaskSqlite($"Data Source={_dbPath}{PoolingOff}", o => { o.Retry.Enabled = true; o.Retry.Timeout = TimeSpan.FromSeconds(30); });
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<ISqlite>();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    public async Task All_concurrent_immediate_writers_commit(int writers)
    {
        // Task.Run forces every writer onto the thread pool at once, so they genuinely contend for the
        // single write lock (a lazy Select would start them one-by-one, each finishing before the next).
        var tasks = Enumerable.Range(0, writers).Select(worker =>
            Task.Run(() => _factory.InImmediateTransactionAsync(async (connection, ct) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO writes(worker) VALUES ($worker);";
                command.Parameters.AddWithValue("$worker", worker);
                await command.ExecuteNonQueryAsync(ct);
            })));

        await Task.WhenAll(tasks);

        // Every writer committed exactly once — the whole burst serialized on the write lock with no loss.
        Assert.Equal(writers, CountRows());
        Assert.Equal(writers, DistinctWorkers());
    }

    [Fact]
    public async Task Writers_far_exceeding_the_thread_pool_do_not_deadlock()
    {
        // Fewer worker threads than writers: a thread-blocking wait would starve here. Shrink the pool to
        // make the point, then restore it.
        ThreadPool.GetMinThreads(out var minWorker, out var minIo);
        ThreadPool.GetMaxThreads(out var maxWorker, out var maxIo);
        ThreadPool.SetMinThreads(4, minIo);
        ThreadPool.SetMaxThreads(8, maxIo);
        try
        {
            const int writers = 400;
            var tasks = Enumerable.Range(0, writers).Select(worker =>
                Task.Run(() => _factory.InImmediateTransactionAsync(async (connection, ct) =>
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "INSERT INTO writes(worker) VALUES ($worker);";
                    command.Parameters.AddWithValue("$worker", worker);
                    await command.ExecuteNonQueryAsync(ct);
                })));

            // With only 8 worker threads, this completes only because the wait yields the thread.
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(writers, CountRows());
        }
        finally
        {
            ThreadPool.SetMinThreads(minWorker, minIo);
            ThreadPool.SetMaxThreads(maxWorker, maxIo);
        }
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long CountRows() => Scalar("SELECT COUNT(*) FROM writes;");

    private long DistinctWorkers() => Scalar("SELECT COUNT(DISTINCT worker) FROM writes;");

    private long Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}{PoolingOff}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        // No ClearAllPools() here, deliberately. Nothing this class opens is pooled, so it has no pooled
        // handle to release — and calling it anyway would make this class the PERPETRATOR of the same race
        // against every other class holding live connections in the shared testhost. Disposing an unpooled
        // connection closes its sqlite3 outright, which is what frees the file for the delete below.
        _provider.Dispose();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-shm", $"{_dbPath}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
