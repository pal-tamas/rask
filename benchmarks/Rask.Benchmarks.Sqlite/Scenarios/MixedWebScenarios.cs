using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Benchmarks.Sqlite.Db;
using Rask.SQLite;

namespace Rask.Benchmarks.Sqlite.Scenarios;

// Workload C — the shape of real web traffic: ~90% reads, ~10% writes, against a seeded table. This is the
// arm whose number answers "can one server and one SQLite file carry a real app?", so it is the one that has
// to look like an app rather than a microbenchmark: a list page plus a row fetch for a read, an insert for a
// write, over 10k seeded rows with the index a real app would have.

internal abstract class MixedScenario(string label) : LoadScenario
{
    private protected const int SeedRows = 10_000;

    private protected const string PostsSchema =
        """
        CREATE TABLE posts(
            id INTEGER PRIMARY KEY,
            title TEXT NOT NULL,
            body TEXT NOT NULL,
            created_at INTEGER NOT NULL);
        CREATE INDEX ix_posts_created ON posts(created_at DESC);
        """;

    private protected LoadDb Db { get; } = new(label);

    internal override string DbPath => Db.Path;

    /// <summary>Reads against an empty table measure the page cache, not the database — so seed it.</summary>
    private protected void CreateAndSeed()
    {
        Db.Create(PostsSchema);
        using var connection = new SqliteConnection(Db.ConnectionString);
        connection.Open();
        LoadDb.Exec(connection, "PRAGMA busy_timeout=5000;");
        LoadDb.Exec(
            connection,
            $"""
             INSERT INTO posts(title, body, created_at)
             WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < {SeedRows})
             SELECT 'seed ' || n, 'body ' || n, n FROM seq;
             """);
    }

    /// <summary>
    /// One VU's own generator, seeded by VU id: reproducible across runs, and no shared state to contend on.
    /// </summary>
    private protected static Random RandomFor(int vuser) => new(vuser * 7919);

    internal override Task<ScenarioInvariants?> VerifyAsync() => Task.FromResult<ScenarioInvariants?>(null);

    internal override Task TeardownAsync()
    {
        Db.Delete();
        return Task.CompletedTask;
    }
}

/// <summary>The raw ADO path: 90% list-page + row fetch, 10% insert through the non-blocking write.</summary>
internal sealed class MixedRawScenario : MixedScenario
{
    private readonly ConcurrentDictionary<int, Random> _random = new();
    private readonly bool _pinWal;
    private ServiceProvider? _provider;
    private IRaskSqliteConnectionFactory? _factory;
    private SqliteConnection? _pinnedReader;

    /// <param name="name">Arm id — the soak reuses this workload under its own names.</param>
    /// <param name="pinWal">
    /// Holds one read transaction open for the whole run. A reader pins the WAL's oldest needed frame, so
    /// checkpointing cannot reclaim and the WAL grows without bound — the only way to actually exercise
    /// <c>journal_size_limit</c>, since a healthy database auto-checkpoints at ~4MB and never approaches it.
    /// </param>
    internal MixedRawScenario(string name = "mixed-raw", bool pinWal = false)
        : base(name)
    {
        Name = name;
        _pinWal = pinWal;
    }

    internal override string Name { get; }

    internal override Task SetupAsync(CancellationToken cancellationToken)
    {
        CreateAndSeed();
        var services = new ServiceCollection();
        services.AddRaskSqlite(Db.ConnectionString, configureRetry: r => r.Timeout = TimeSpan.FromSeconds(30));
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<IRaskSqliteConnectionFactory>();


        if (_pinWal)
        {
            _pinnedReader = new SqliteConnection(Db.ConnectionString);
            _pinnedReader.Open();
            LoadDb.Exec(_pinnedReader, "BEGIN;");
            LoadDb.Exec(_pinnedReader, "SELECT COUNT(*) FROM posts;");
        }

        return Task.CompletedTask;
    }

    internal override async ValueTask<OpOutcome> ExecuteAsync(int vuser, CancellationToken cancellationToken)
    {
        var random = _random.GetOrAdd(vuser, RandomFor);
        var write = random.Next(100) < 10;

        if (write)
        {
            await _factory!.ExecuteInImmediateTransactionAsync(async (connection, ct) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO posts(title, body, created_at) VALUES ($t, $b, $c);";
                command.Parameters.AddWithValue("$t", "posted");
                command.Parameters.AddWithValue("$b", "body");
                command.Parameters.AddWithValue("$c", SeedRows + random.Next(SeedRows));
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);

            return OpOutcome.Ok;
        }

        await using var connection = await _factory!.CreateOpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT id, title FROM posts ORDER BY created_at DESC LIMIT 20;";
            await using var reader = await list.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                _ = reader.GetInt32(0);
            }
        }

        await using (var single = connection.CreateCommand())
        {
            single.CommandText = "SELECT id, title, body FROM posts WHERE id = $id;";
            single.Parameters.AddWithValue("$id", random.Next(1, SeedRows));
            await using var reader = await single.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                _ = reader.GetInt32(0);
            }
        }

        return OpOutcome.Ok;
    }

    internal override async Task TeardownAsync()
    {
        if (_pinnedReader is not null)
        {
            // Release the pin and checkpoint once, so the caller can see the WAL truncate back down.
            LoadDb.Exec(_pinnedReader, "COMMIT;");
            LoadDb.Exec(_pinnedReader, "PRAGMA wal_checkpoint(TRUNCATE);");
            await _pinnedReader.DisposeAsync().ConfigureAwait(false);
        }

        if (_provider is not null)
        {
            await _provider.DisposeAsync().ConfigureAwait(false);
        }

        await base.TeardownAsync().ConfigureAwait(false);
    }
}

/// <summary>The same traffic through EF Core, with a fresh context per operation as a web app would.</summary>
internal sealed class MixedEfScenario() : MixedScenario("mixed-ef")
{
    private readonly ConcurrentDictionary<int, Random> _random = new();
    private DbContextOptions<PostsDbContext>? _options;

    internal override string Name => "mixed-ef";

    internal override Task SetupAsync(CancellationToken cancellationToken)
    {
        CreateAndSeed();
        _options = new DbContextOptionsBuilder<PostsDbContext>()
            .UseRaskSqlite(Db.ConnectionString, configureRetry: r => r.Timeout = TimeSpan.FromSeconds(30))
            .Options;


        return Task.CompletedTask;
    }

    internal override async ValueTask<OpOutcome> ExecuteAsync(int vuser, CancellationToken cancellationToken)
    {
        var random = _random.GetOrAdd(vuser, RandomFor);
        await using var context = new PostsDbContext(_options!);

        if (random.Next(100) < 10)
        {
            context.Posts.Add(new Post
            {
                Title = "posted",
                Body = "body",
                CreatedAt = SeedRows + random.Next(SeedRows),
            });
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OpOutcome.Ok;
        }

        // AsNoTracking: a read-only page has no reason to pay for the change tracker, which is what a real
        // query would do here.
        _ = await context.Posts
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Take(20)
            .Select(p => new { p.Id, p.Title })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The id must be drawn before the query: inside the expression tree EF would try to translate
        // Random.Next into SQL and throw instead of running the read.
        var id = random.Next(1, SeedRows);
        _ = await context.Posts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return OpOutcome.Ok;
    }
}

internal static class MixedScenarios
{
    internal static IReadOnlyList<Func<LoadScenario>> All =>
    [
        () => new MixedRawScenario(),
        () => new MixedEfScenario(),
    ];

    /// <summary>
    /// Workload D — the same traffic held for minutes, which is the only way to see what a 30-second run
    /// cannot: WAL growth against the 64MiB journal_size_limit, checkpoint stalls in the per-window tail, and
    /// latency drift.
    /// </summary>
    internal static IReadOnlyList<Func<LoadScenario>> Soak =>
    [
        () => new MixedRawScenario("soak-mixed"),
        () => new MixedRawScenario("soak-wal-pinned", pinWal: true),
    ];
}
