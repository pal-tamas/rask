namespace Rask.Dashboard.Panels;

/// <summary>One cache entry, without its value.</summary>
/// <param name="Key">The cache key.</param>
/// <param name="Bytes">Size of the stored value.</param>
/// <param name="CreatedAt">When it was written (UTC).</param>
/// <param name="ExpiresAt">When it stops being served (UTC).</param>
/// <param name="SlidingSeconds">The sliding window, if it has one.</param>
public sealed record CacheKeyRow(string Key, int Bytes, DateTime CreatedAt, DateTime ExpiresAt, double? SlidingSeconds);
