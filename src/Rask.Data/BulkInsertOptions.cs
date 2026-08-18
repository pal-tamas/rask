namespace Rask.Data;

/// <summary>
/// Options for <see cref="BulkInsertExtensions.BulkInsertAsync{TEntity}(Microsoft.EntityFrameworkCore.DbContext, System.Collections.Generic.IEnumerable{TEntity}, System.Action{BulkInsertOptions}?, System.Threading.CancellationToken)"/>.
/// </summary>
public sealed class BulkInsertOptions
{
    /// <summary>The largest <see cref="BatchSize"/> accepted — past this the change tracker is the cost.</summary>
    internal const int MaxBatchSize = 100_000;

    private int _batchSize = 5_000;

    /// <summary>
    /// How many entities are tracked and saved per round-trip. Each batch is one <c>SaveChanges</c>, after
    /// which the change tracker is cleared — which is what keeps a million-row load flat in memory instead
    /// of quadratic. Defaults to 5,000; must be between 1 and 100,000.
    /// </summary>
    public int BatchSize
    {
        get => _batchSize;
        set => _batchSize = value;
    }

    /// <summary>
    /// Whether the whole load commits as <b>one</b> transaction (<c>true</c>) or each batch commits on its
    /// own (<c>false</c>, the default).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-batch is the default because a single transaction over a large load is a poor trade on SQLite:
    /// one writer holds the database's only write lock for the entire import, so every other writer in the
    /// application waits it out, and the WAL grows to hold every uncommitted page. Committing per batch
    /// hands the lock back between batches. The cost is that a failure part-way leaves the batches that
    /// already committed — which for a seed or an import is usually the retryable outcome you want.
    /// </para>
    /// <para>
    /// Set this to <c>true</c> when the load must be all-or-nothing. Entities carrying domain events are
    /// then <b>rejected</b>: <see cref="DomainEventInterceptor"/> publishes in <c>SavedChanges</c>, which
    /// inside a transaction happens before the commit — so a load that failed later would have already
    /// announced rows that no longer exist. Use <c>Rask.Outbox</c> when an atomic load must raise events;
    /// its messages are written in the same transaction and drained after it commits.
    /// </para>
    /// </remarks>
    public bool SingleTransaction { get; set; }

    /// <summary>
    /// Whether to write the rows straight to the provider with a prepared <c>INSERT</c> instead of through
    /// the change tracker. Off by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the fast path: over 100,000 rows on SQLite it runs in roughly a quarter of the time and
    /// allocates about an eighth as much, because no entity entry is ever materialised and the provider parses
    /// one statement for the whole load.
    /// </para>
    /// <para>
    /// It is opt-in because of what it skips. <b>No <c>ISaveChangesInterceptor</c> runs</b> — not Rask.Data's,
    /// and not any you registered yourself. The writer stamps <see cref="ITimestamped"/> audit columns in
    /// <see cref="AuditingInterceptor"/>'s place, but nothing stands in for the rest: entities carrying domain
    /// events are rejected rather than inserted with their events undelivered, and an outbox never sees the
    /// load. Anything the writer cannot map faithfully — store-generated keys, shadow properties, navigations,
    /// an inheritance hierarchy — throws and names the reason instead of quietly writing the wrong rows.
    /// </para>
    /// </remarks>
    public bool SkipChangeTracking { get; set; }

    /// <summary>Throws if any option is out of range. Called at the start of every bulk insert.</summary>
    internal void Validate()
    {
        if (_batchSize is < 1 or > MaxBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BatchSize), _batchSize, $"BatchSize must be between 1 and {MaxBatchSize}.");
        }
    }
}
