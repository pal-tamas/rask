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
    private readonly ServiceProvider _provider;
    private readonly ISqlite _factory;

    public SqliteConcurrencyStressTests()
    {
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            Exec(connection, "PRAGMA journal_mode=WAL;");
            Exec(connection, "CREATE TABLE writes(id INTEGER PRIMARY KEY, worker INTEGER NOT NULL);");
        }

        var services = new ServiceCollection();
        // A generous per-writer timeout so a busy CI box under heavy contention never spuriously times out.
        services.AddRaskSqlite($"Data Source={_dbPath}", o => { o.Retry.Enabled = true; o.Retry.Timeout = TimeSpan.FromSeconds(30); });
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
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        _provider.Dispose();
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
