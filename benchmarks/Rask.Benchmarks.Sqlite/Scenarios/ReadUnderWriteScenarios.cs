using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Rask.Benchmarks.Sqlite.Db;
using Rask.SQLite;

namespace Rask.Benchmarks.Sqlite.Scenarios;

// Workload B — "readers don't block the writer", the headline WAL claim. Measuring WAL alone would prove
// nothing: fast readers are only meaningful against the journal mode that WAL replaced. So each reader sweep
// runs three ways — no writer at all (the baseline), WAL with a writer, and DELETE (rollback journal) with a
// writer. The result is two ratios from one run on one box, which is what makes the claim checkable on
// hardware that disagrees wildly about absolute milliseconds.

/// <summary>
/// Readers hitting the newest rows while <see cref="LoadOptions.Writers"/> writers hammer the same database.
/// </summary>
internal sealed class ReadUnderWriteScenario : LoadScenario
{
    private const string Select = "SELECT id, worker FROM writes ORDER BY id DESC LIMIT 20;";

    private readonly string _journalMode;
    private readonly int _writers;
    private readonly LoadDb _db;
    private readonly List<Task> _writerTasks = [];
    private CancellationTokenSource? _writerCts;
    private ServiceProvider? _provider;
    private IRaskSqliteConnectionFactory? _factory;

    internal ReadUnderWriteScenario(string name, string journalMode, int writers)
    {
        Name = name;
        _journalMode = journalMode;
        _writers = writers;
        _db = new LoadDb(name);
    }

    internal override string Name { get; }

    internal override string DbPath => _db.Path;

    /// <summary>Throughput the background writers sustained — in DELETE mode the writer suffers too.</summary>
    internal long WriterCommits;

    internal override Task SetupAsync(CancellationToken cancellationToken)
    {
        _db.Create(WriteScenarios.WritesSchema, _journalMode);

        // Seed, so readers return real rows from the first operation rather than measuring an empty table.
        using (var connection = new SqliteConnection(_db.ConnectionString))
        {
            connection.Open();
            LoadDb.Exec(connection, "PRAGMA busy_timeout=5000;");
            LoadDb.Exec(
                connection,
                """
                INSERT INTO writes(worker, payload)
                WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 5000)
                SELECT 0, 'seed' FROM seq;
                """);
        }

        var services = new ServiceCollection();
        services.AddRaskSqlite(
            _db.ConnectionString,
            configure: o => o.JournalMode = _journalMode == "WAL" ? SqliteJournalMode.Wal : SqliteJournalMode.Delete,
            configureRetry: r => r.Timeout = TimeSpan.FromSeconds(30));
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<IRaskSqliteConnectionFactory>();

        // The writers are load, not measurement: they run outside the VU pool and are never timed. Only the
        // readers' latency is the answer to "do readers block?".
        _writerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        for (var i = 0; i < _writers; i++)
        {
            var worker = i + 1;
            _writerTasks.Add(Task.Run(() => DriveWriterAsync(worker, _writerCts.Token), CancellationToken.None));
        }

        return Task.CompletedTask;
    }

    private async Task DriveWriterAsync(int worker, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _factory!.ExecuteInImmediateTransactionAsync(async (connection, ct) =>
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "INSERT INTO writes(worker, payload) VALUES ($worker, 'w');";
                    command.Parameters.AddWithValue("$worker", worker);
                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);

                Interlocked.Increment(ref WriterCommits);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A writer failing under DELETE-mode contention is the expected half of the story, not a
                // harness fault — keep pushing so the readers stay under pressure.
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal override async ValueTask<OpOutcome> ExecuteAsync(int vuser, CancellationToken cancellationToken)
    {
        await using var connection = await _factory!.CreateOpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = Select;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            _ = reader.GetInt32(0);
        }

        return OpOutcome.Ok;
    }

    // Readers write nothing, so there is no lost-write invariant to check here.
    internal override Task<ScenarioInvariants?> VerifyAsync() => Task.FromResult<ScenarioInvariants?>(null);

    internal override async Task TeardownAsync()
    {
        if (_writerCts is not null)
        {
            await _writerCts.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(_writerTasks).ConfigureAwait(false);
            _writerCts.Dispose();
        }

        if (_provider is not null)
        {
            await _provider.DisposeAsync().ConfigureAwait(false);
        }

        _db.Delete();
    }
}

internal static class ReadScenarios
{
    internal static IReadOnlyList<Func<LoadScenario>> For(int writers) =>
    [
        // The baseline: what a reader costs when nothing is writing.
        () => new ReadUnderWriteScenario("wal-readers-only", "WAL", 0),

        // The claim: readers should stay at ~baseline even with the writer running.
        () => new ReadUnderWriteScenario("wal-read-under-write", "WAL", writers),

        // The control: the rollback journal, where readers and the writer exclude each other. Without this
        // arm, "WAL readers are fast" is a number with nothing to be fast compared to.
        () => new ReadUnderWriteScenario("delete-read-under-write", "DELETE", writers),
    ];
}
