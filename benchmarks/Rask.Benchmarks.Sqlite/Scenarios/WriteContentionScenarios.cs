using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Benchmarks.Sqlite.Db;
using Rask.SQLite;

namespace Rask.Benchmarks.Sqlite.Scenarios;

// Workload A — the write path under contention, four ways against one WAL database. Every arm does the same
// INSERT in one transaction; what differs is who waits for the write lock and how. This is the head-to-head
// that turns docs/sqlite.md's prose into numbers.

/// <summary>Shared plumbing: one private database, the same INSERT, the same read-back.</summary>
internal abstract class WriteScenario(string label) : LoadScenario
{
    protected const string Insert = "INSERT INTO writes(worker, payload) VALUES ($worker, $payload);";
    protected const string Payload = "rask-load";

    protected LoadDb Db { get; } = new(label);

    internal override string DbPath => Db.Path;

    internal override Task<ScenarioInvariants?> VerifyAsync() => Task.FromResult<ScenarioInvariants?>(
        new ScenarioInvariants(
            Db.Scalar("SELECT COUNT(*) FROM writes;"),
            Db.Scalar("SELECT COUNT(DISTINCT worker) FROM writes;")));

    internal override Task TeardownAsync()
    {
        Db.Delete();
        return Task.CompletedTask;
    }
}

/// <summary>
/// The recommendation: <c>ExecuteInImmediateTransactionAsync</c> takes the write lock through the raw
/// sqlite3 handle with the native busy handler off, and awaits a constant fair interval between attempts —
/// so a contended writer frees its thread instead of pinning it.
/// </summary>
internal sealed class RawNonBlockingScenario() : WriteScenario("raw-nonblocking")
{
    private ServiceProvider? _provider;
    private IRaskSqliteConnectionFactory? _factory;

    internal override string Name => "raw-nonblocking";

    internal override Task SetupAsync(CancellationToken cancellationToken)
    {
        Db.Create(WriteScenarios.WritesSchema);

        // Its own ServiceCollection: AddRaskSqlite is idempotent per collection, so a shared one would
        // silently bind every arm to the first arm's database.
        var services = new ServiceCollection();
        services.AddRaskSqlite(Db.ConnectionString, configureRetry: r => r.Timeout = TimeSpan.FromSeconds(30));
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<IRaskSqliteConnectionFactory>();
        return Task.CompletedTask;
    }

    internal override async ValueTask<OpOutcome> ExecuteAsync(int vuser, CancellationToken cancellationToken)
    {
        await _factory!.ExecuteInImmediateTransactionAsync(async (connection, ct) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Insert;
            command.Parameters.AddWithValue("$worker", vuser);
            command.Parameters.AddWithValue("$payload", Payload);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return OpOutcome.Ok;
    }

    internal override async Task TeardownAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync().ConfigureAwait(false);
        }

        await base.TeardownAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// The classic path: <c>BEGIN IMMEDIATE</c> with SQLite's native <c>busy_timeout</c>, so a contended writer
/// blocks its thread inside Microsoft.Data.Sqlite until the lock frees. Expected to fall off a cliff once
/// the writers outnumber the pool's threads.
/// </summary>
internal sealed class RawNativeBusyTimeoutScenario() : WriteScenario("raw-native-busytimeout")
{
    internal override string Name => "raw-native-busytimeout";

    internal override Task SetupAsync(CancellationToken cancellationToken)
    {
        Db.Create(WriteScenarios.WritesSchema);
        return Task.CompletedTask;
    }

    internal override async ValueTask<OpOutcome> ExecuteAsync(int vuser, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(Db.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Hand-built, so no factory StateChange hook re-applies the pragmas — this arm must set its own on
        // every open, exactly as the equivalent BenchmarkDotNet arm does.
        LoadDb.Exec(connection, "PRAGMA busy_timeout=5000;");

        using var transaction = connection.BeginImmediate(); // waits by blocking the thread
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = Insert;
            command.Parameters.AddWithValue("$worker", vuser);
            command.Parameters.AddWithValue("$payload", Payload);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        return OpOutcome.Ok;
    }
}

/// <summary>
/// EF Core with the fair-interval strategy enabled. A new context per operation, which is what a web app
/// does (a scoped context per request).
/// </summary>
internal sealed class EfScenario : WriteScenario
{
    private readonly bool _retry;
    private DbContextOptions<WritesDbContext>? _options;

    internal EfScenario(bool retry)
        : base(retry ? "ef-retry" : "ef-no-retry") => _retry = retry;

    internal override string Name => _retry ? "ef-retry" : "ef-no-retry";

    internal override Task SetupAsync(CancellationToken cancellationToken)
    {
        Db.Create(WriteScenarios.WritesSchema);

        // Passing configureRetry AT ALL is the switch: it zeroes the native busy handler, lowers the
        // driver's blocking command timeout, and registers RaskSqliteExecutionStrategy. Omitting it is the
        // negative control.
        var builder = new DbContextOptionsBuilder<WritesDbContext>();
        _options = _retry
            ? builder.UseRaskSqlite(
                Db.ConnectionString,
                configureRetry: r => r.Timeout = TimeSpan.FromSeconds(30)).Options
            : builder.UseRaskSqlite(Db.ConnectionString).Options;

        return Task.CompletedTask;
    }

    internal override async ValueTask<OpOutcome> ExecuteAsync(int vuser, CancellationToken cancellationToken)
    {
        await using var context = new WritesDbContext(_options!);
        context.Writes.Add(new WriteRow { Worker = vuser, Payload = Payload });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OpOutcome.Ok;
    }
}

internal static class WriteScenarios
{
    internal const string WritesSchema =
        "CREATE TABLE writes(id INTEGER PRIMARY KEY, worker INTEGER NOT NULL, payload TEXT NOT NULL);";

    internal static IReadOnlyList<Func<LoadScenario>> All =>
    [
        () => new RawNonBlockingScenario(),
        () => new RawNativeBusyTimeoutScenario(),
        () => new EfScenario(retry: true),
        () => new EfScenario(retry: false),
    ];
}
