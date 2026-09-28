namespace Rask.Dashboard.Panels;

/// <summary>
/// The mutations a queue panel can perform.
/// <para>
/// Every one is a single <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> statement with its guard in the
/// <c>WHERE</c> clause — never a tracked entity. That matters for two reasons: the dashboard can't itself
/// raise a concurrency exception, and the guard is evaluated by the database at the moment of the write
/// rather than by the dashboard a few hundred milliseconds earlier.
/// </para>
/// </summary>
public interface IQueueActions
{
    /// <summary>
    /// Puts a dead letter back in the queue: <c>Attempts = 0</c>, <c>RunAt = now</c>, <c>Error = null</c>.
    /// <para>
    /// Guarded by <c>ProcessedAt IS NULL AND Attempts &gt;= MaxAttempts</c> — the exact set the drain
    /// query excludes. A row a processor could currently be holding is therefore untouchable by this
    /// operation, so it needs no coordination with the drain at all. Returns rows affected.
    /// </para>
    /// </summary>
    Task<int> RetryAsync(long id, CancellationToken cancellationToken);

    /// <summary>Retries every dead letter in this queue. Same guard, no id.</summary>
    Task<int> RetryAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes completed rows older than <paramref name="olderThan"/>. Guarded on
    /// <c>ProcessedAt IS NOT NULL</c>, so nothing outstanding — and no dead letter — is ever removed.
    /// </summary>
    Task<int> PurgeProcessedAsync(TimeSpan olderThan, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes one outstanding row, destroying the work. Guarded on <c>ProcessedAt IS NULL</c> so a
    /// completed row's record is never lost; requires <see cref="RaskDashboardActions.Destructive"/>.
    /// </summary>
    Task<int> DeleteAsync(long id, CancellationToken cancellationToken);
}
