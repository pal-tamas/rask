namespace Rask.Dashboard.Panels;

/// <summary>
/// The cache reader, without the context type parameter — pages aren't generic, so they resolve this.
/// </summary>
public interface ICachePanelReader
{
    /// <summary><c>false</c> when Rask.Cache isn't registered or its table isn't mapped.</summary>
    bool IsAvailable { get; }

    /// <summary>Entry count, total stored bytes, and how many are expired but not yet swept.</summary>
    Task<CacheStats> StatsAsync(CancellationToken cancellationToken);

    /// <summary>One page of keys, soonest to expire first.</summary>
    Task<(IReadOnlyList<CacheKeyRow> Rows, int Total)> PageAsync(
        string? search, int skip, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Drops one key. Safe by nature — a cache miss is a recompute, not a lost fact — which is why this
    /// sits in the Safe action tier while flushing everything does not.
    /// </summary>
    Task<int> EvictAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Drops every entry. Correctness-safe for the same reason, but a cold cache on a busy app means a
    /// stampede of recomputes, so it needs <see cref="RaskDashboardActions.Destructive"/>.
    /// </summary>
    Task<int> FlushAsync(CancellationToken cancellationToken);
}
