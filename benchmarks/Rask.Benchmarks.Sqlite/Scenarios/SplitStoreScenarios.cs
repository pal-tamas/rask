using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Rask.Benchmarks.Sqlite.Db;
using Rask.SQLite;

namespace Rask.Benchmarks.Sqlite.Scenarios;

// Workload E — one file or several? Rask maps the cache, the job queue and the app's own tables into one
// DbContext, so today they share one database file. SQLite's write lock is per *file*, which means the cache
// purge sweep and the job-claim batch take the same lock a user request needs. This workload measures what
// moving them to their own files is worth: identical app writers, identical background churn, the only
// difference being whether the churn lands in the app's file or beside it.
//
// The measured population is the app writers alone. The churn is ambient — it runs on the scenario's own
// tasks, not on virtual users — so throughput and latency describe what a *request* sees, which is the
// number the question is actually about.
//
// The app arm's INSERT and schema are deliberately identical to workload A's `raw-nonblocking`, so a row here
// can be read directly against the corresponding row there: the difference is the churn and nothing else.

/// <summary>How hard the background batteries push while the app writes.</summary>
internal enum ChurnLevel
{
    /// <summary>
    /// The shipped defaults on a quiet app: <c>JobOptions.PollInterval</c> 5s, <c>CacheOptions.PurgeInterval</c>
    /// 5min. The control arm — at this cadence the purge does not fire at all inside a 15s measurement, which
    /// is precisely why a quiet app has nothing to gain from splitting.
    /// </summary>
    Idle,

    /// <summary>
    /// A loaded single-server app: ~500 cache writes/s and ~500 enqueues/s, with the sweep and the claim
    /// batch <b>compressed</b> (purge 2s rather than 5min, poll 500ms rather than 5s) so several of each land
    /// inside the measured window. Compression is what makes the effect observable in 15 seconds; it also
    /// means this arm reports the shape of the cost, not its everyday frequency.
    /// </summary>
    Busy,
}

/// <summary>The cadences one <see cref="ChurnLevel"/> drives its four background loops at.</summary>
internal sealed record ChurnProfile(TimeSpan CacheWrite, TimeSpan Purge, TimeSpan Enqueue, TimeSpan Poll)
{
    internal static ChurnProfile For(ChurnLevel level) => level switch
    {
        ChurnLevel.Idle => new ChurnProfile(
            CacheWrite: TimeSpan.FromMilliseconds(100),
            Purge: TimeSpan.FromMinutes(5),
            Enqueue: TimeSpan.FromMilliseconds(100),
            Poll: TimeSpan.FromSeconds(5)),
        ChurnLevel.Busy => new ChurnProfile(
            CacheWrite: TimeSpan.FromMilliseconds(2),
            Purge: TimeSpan.FromSeconds(2),
            Enqueue: TimeSpan.FromMilliseconds(2),
            Poll: TimeSpan.FromMilliseconds(500)),
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };
}

/// <summary>
/// App writers plus battery churn, against either one database file or three. The app's own file is
/// <see cref="DbPath"/> — the one the WAL monitor watches and the one whose lock the measurement is about.
/// </summary>
internal sealed class SplitStoreScenario : LoadScenario
{
    /// <summary>Cache rows seeded up front, so the purge sweep has a realistic amount to delete.</summary>
    private const int CacheSeedRows = 50_000;

    /// <summary>Expiry buckets the seed is spread across — one bucket's worth falls due per second.</summary>
    private const int ExpiryBuckets = 20;

    /// <summary>Rows a poll claims at once. <c>JobOptions.BatchSize</c>'s default, which is the point.</summary>
    private const int ClaimBatch = 100;

    private const string CacheSchema =
        """
        CREATE TABLE cache_entries(
            key TEXT PRIMARY KEY,
            value BLOB NOT NULL,
            expires_at INTEGER NOT NULL);
        CREATE INDEX ix_cache_expires ON cache_entries(expires_at);
        """;

    private const string QueueSchema =
        """
        CREATE TABLE jobs(
            id INTEGER PRIMARY KEY,
            type TEXT NOT NULL,
            payload TEXT NOT NULL,
            run_at INTEGER NOT NULL,
            processed_at INTEGER NULL,
            claim_token TEXT NULL);
        CREATE INDEX ix_jobs_pending ON jobs(processed_at, run_at, id);
        """;

    /// <summary>A cache value the size a real one tends to be — big enough that the sweep moves real pages.</summary>
    private static readonly byte[] CacheValue = new byte[1024];

    private readonly bool _split;
    private readonly ChurnProfile _churn;
    private readonly CancellationTokenSource _churnStop = new();
    private readonly List<Thread> _churnThreads = [];

    private ServiceProvider? _provider;
    private ISqlite? _factory;

    /// <param name="split">
    /// <see langword="true"/> puts the cache and the queue in their own files. <see langword="false"/> is
    /// today's shape: one file, one write lock, everything behind it.
    /// </param>
    internal SplitStoreScenario(bool split, ChurnLevel churn)
    {
        _split = split;
        _churn = ChurnProfile.For(churn);
        Name = $"{(split ? "split" : "one-file")}-{(churn == ChurnLevel.Idle ? "idle" : "busy")}";

        App = new LoadDb(Name);

        // Not splitting means literally the same file — the same LoadDb, so the same connection string and
        // therefore the same Microsoft.Data.Sqlite pool. Anything less would compare two things at once.
        Cache = split ? new LoadDb($"{Name}-cache") : App;
        Queue = split ? new LoadDb($"{Name}-queue") : App;
    }

    internal override string Name { get; }

    private LoadDb App { get; }

    private LoadDb Cache { get; }

    private LoadDb Queue { get; }

    /// <summary>The app's file. The split arm owns three; this is the one the measurement is about.</summary>
    internal override string DbPath => App.Path;

    internal override Task SetupAsync(CancellationToken cancellationToken)
    {
        // One file: create it once with all three schemas, or the second Create would find the file there and
        // re-run CREATE TABLE against it.
        if (_split)
        {
            App.Create(WriteScenarios.WritesSchema);
            Cache.Create(CacheSchema);
            Queue.Create(QueueSchema);
        }
        else
        {
            App.Create($"{WriteScenarios.WritesSchema}\n{CacheSchema}\n{QueueSchema}");
        }

        SeedCache();

        // Its own ServiceCollection: AddRaskSqlite is idempotent per collection, so a shared one would
        // silently bind every arm to the first arm's database.
        var services = new ServiceCollection();
        services.AddRaskSqlite(App.ConnectionString, configureRetry: r => r.Timeout = TimeSpan.FromSeconds(30));
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<ISqlite>();

        // Churn starts before the runner's warmup, so the measured window opens onto a system already in
        // motion rather than onto a cold cache table and an empty queue. Each loop is single-threaded, so its
        // Random needs no synchronisation — and a fixed seed keeps the key distribution the same across arms.
        var cacheKeys = new Random(7919);
        StartChurn(Cache, _churn.CacheWrite, c => WriteCacheEntry(c, cacheKeys));
        StartChurn(Cache, _churn.Purge, PurgeCache);
        StartChurn(Queue, _churn.Enqueue, Enqueue);
        StartChurn(Queue, _churn.Poll, ClaimBatchOfJobs);

        return Task.CompletedTask;
    }

    internal override async ValueTask<OpOutcome> ExecuteAsync(int vuser, CancellationToken cancellationToken)
    {
        await _factory!.InImmediateTransactionAsync(async (connection, ct) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = WriteScenarios.Insert;
            command.Parameters.AddWithValue("$worker", vuser);
            command.Parameters.AddWithValue("$payload", WriteScenarios.Payload);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return OpOutcome.Ok;
    }

    internal override Task<ScenarioInvariants?> VerifyAsync() => Task.FromResult<ScenarioInvariants?>(
        new ScenarioInvariants(
            App.Scalar("SELECT COUNT(*) FROM writes;"),
            App.Scalar("SELECT COUNT(DISTINCT worker) FROM writes;")));

    internal override async Task TeardownAsync()
    {
        await _churnStop.CancelAsync().ConfigureAwait(false);

        // Every churn loop must be off the files before Delete clears the pool underneath it. A loop parked in
        // busy_timeout can take that long to notice, so the join has to outlast it.
        foreach (var thread in _churnThreads)
        {
            thread.Join(TimeSpan.FromSeconds(15));
        }

        _churnStop.Dispose();

        if (_provider is not null)
        {
            await _provider.DisposeAsync().ConfigureAwait(false);
        }

        App.Delete();
        if (_split)
        {
            Cache.Delete();
            Queue.Delete();
        }
    }

    /// <summary>
    /// Fills the cache table and spreads the expiries across <see cref="ExpiryBuckets"/> seconds, so each
    /// sweep finds roughly a bucket's worth due rather than everything at once or nothing at all.
    /// </summary>
    private void SeedCache()
    {
        using var connection = new SqliteConnection(Cache.ConnectionString);
        connection.Open();
        LoadDb.Exec(connection, "PRAGMA busy_timeout=5000;");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        LoadDb.Exec(
            connection,
            string.Create(
                CultureInfo.InvariantCulture,
                $"""
                 INSERT INTO cache_entries(key, value, expires_at)
                 WITH RECURSIVE seq(n) AS (
                     SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < {CacheSeedRows})
                 SELECT 'k' || n, zeroblob(1024), {now} + (n % {ExpiryBuckets}) FROM seq;
                 """));
    }

    private static void WriteCacheEntry(SqliteConnection connection, Random random)
    {
        // INSERT OR REPLACE is what a cache write is: the key is known, the row may or may not be there.
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR REPLACE INTO cache_entries(key, value, expires_at) VALUES ($k, $v, $e);";
        command.Parameters.AddWithValue("$k", $"k{random.Next(CacheSeedRows)}");
        command.Parameters.AddWithValue("$v", CacheValue);
        command.Parameters.AddWithValue(
            "$e", DateTimeOffset.UtcNow.ToUnixTimeSeconds() + random.Next(ExpiryBuckets));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// CachePurger's sweep: one unbounded DELETE across the expiry index. This is the long lock hold the
    /// whole workload exists to price.
    /// </summary>
    private static void PurgeCache(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM cache_entries WHERE expires_at < $now;";
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        command.ExecuteNonQuery();
    }

    private static void Enqueue(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO jobs(type, payload, run_at) VALUES ('Bench.Job', $p, $r);";
        command.Parameters.AddWithValue("$p", "{}");
        command.Parameters.AddWithValue("$r", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// JobProcessor's claim: stamp a batch of due rows in one statement, the second long lock hold.
    /// </summary>
    private static void ClaimBatchOfJobs(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE jobs SET claim_token = $t, processed_at = $now
            WHERE id IN (
                SELECT id FROM jobs
                WHERE processed_at IS NULL AND run_at <= $now
                ORDER BY run_at, id
                LIMIT $limit);
            """;
        command.Parameters.AddWithValue("$t", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$limit", ClaimBatch);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Drives one background loop on its own connection at a fixed cadence until the arm tears down. It runs
    /// on a task rather than a virtual user on purpose: a VU's time is measured, and the churn is not what we
    /// are measuring.
    /// <para>
    /// A churn failure is written to stderr immediately rather than swallowed. If it ever prints, the arm's
    /// row is not comparable to its sibling's — the two were not carrying the same background load.
    /// </para>
    /// </summary>
    private void StartChurn(LoadDb db, TimeSpan cadence, Action<SqliteConnection> work)
    {
        var cancellationToken = _churnStop.Token;
        var thread = new Thread(() =>
        {
            // One long-lived connection, hand-built, so it sets its own busy_timeout: a churn loop that threw
            // SQLITE_BUSY would quietly stop pushing and flatter the one-file arm.
            using var connection = new SqliteConnection(db.ConnectionString);
            connection.Open();
            LoadDb.Exec(connection, "PRAGMA busy_timeout=5000;");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    work(connection);

                    // WaitOne, not Task.Delay: this thread is the churn's own, so blocking it is free and
                    // cancellation still wakes it immediately.
                    cancellationToken.WaitHandle.WaitOne(cadence);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[churn] {Name}: {ex.GetType().Name}: {ex.Message}");
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = $"churn-{Name}",
        };

        thread.Start();
        _churnThreads.Add(thread);
    }
}

internal static class SplitStoreScenarios
{
    /// <summary>
    /// Both topologies at both churn levels. The idle pair is the control: if it shows a gap, the gap is
    /// noise rather than the split, and the busy pair's gap cannot be trusted either.
    /// </summary>
    internal static IReadOnlyList<Func<LoadScenario>> All =>
    [
        () => new SplitStoreScenario(split: false, ChurnLevel.Idle),
        () => new SplitStoreScenario(split: true, ChurnLevel.Idle),
        () => new SplitStoreScenario(split: false, ChurnLevel.Busy),
        () => new SplitStoreScenario(split: true, ChurnLevel.Busy),
    ];
}
