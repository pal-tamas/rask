namespace Rask.Benchmarks.Sqlite;

/// <summary>How a single operation ended. A readonly struct so the success path allocates nothing.</summary>
internal readonly record struct OpOutcome(OutcomeKind Kind, int SqliteErrorCode = 0, string? ErrorType = null)
{
    internal static readonly OpOutcome Ok = new(OutcomeKind.Ok);
}

internal enum OutcomeKind
{
    /// <summary>Committed. Counted in throughput; latency recorded.</summary>
    Ok,

    /// <summary>An <b>escaped</b> SQLITE_BUSY/SQLITE_LOCKED — the retry loop gave up. The headline error.</summary>
    Busy,

    /// <summary>Any other SQLite error — a real bug, not contention.</summary>
    SqliteError,

    /// <summary>Anything else.</summary>
    Other,

    /// <summary>The measurement deadline hit mid-operation. Neither success nor error; latency discarded.</summary>
    Cancelled,
}

/// <summary>Post-run correctness check: the load is only meaningful if nothing was silently lost.</summary>
internal sealed record ScenarioInvariants(long RowsWritten, long DistinctWorkers);

/// <summary>
/// One arm of a workload. An arm owns its <b>own database file</b> and its <b>own</b>
/// <see cref="IServiceProvider"/>, which is what keeps arms from contaminating each other:
/// Microsoft.Data.Sqlite keys its connection pool by connection string (so separate files ⇒ separate pools),
/// and <c>AddRaskSqlite</c> is idempotent per <c>ServiceCollection</c> — a second call is a silent no-op and
/// the first connection string wins, so one collection could never host two arms.
/// Arms are run <b>serially</b> by <see cref="LoadRunner"/>: <c>SqliteConnection.ClearAllPools()</c> is
/// process-global, so a concurrent arm would have its pool cleared out from under it.
/// </summary>
internal abstract class LoadScenario
{
    /// <summary>Arm id as it appears in the report, e.g. <c>raw-nonblocking</c>.</summary>
    internal abstract string Name { get; }

    /// <summary>This arm's database file — its own, which is what gives it its own connection pool.</summary>
    internal abstract string DbPath { get; }

    /// <summary>Creates the database file, schema and seed data, and builds the arm's service provider.</summary>
    internal abstract Task SetupAsync(CancellationToken cancellationToken);

    /// <summary>Runs exactly one operation. Called concurrently by every virtual user.</summary>
    internal abstract ValueTask<OpOutcome> ExecuteAsync(int vuser, CancellationToken cancellationToken);

    /// <summary>
    /// Reads back what actually landed, so the runner can prove no writes were lost. Null for an arm that
    /// writes nothing.
    /// </summary>
    internal abstract Task<ScenarioInvariants?> VerifyAsync();

    /// <summary>Clears the pool and deletes the database and its <c>-wal</c>/<c>-shm</c> sidecars.</summary>
    internal abstract Task TeardownAsync();
}
