using Microsoft.Extensions.Caching.Distributed;

namespace Rask.Example.Shop.Features.Products;

/// <summary>
/// A read-through cache over one expensive read. Owns its key and its expiry in one place, so the
/// value can be invalidated from anywhere without a caller having to know how the key is built.
/// </summary>
public sealed class PopularProducts(ICache cache)
{
    // Version the key rather than mutating it in place: change the suffix when the shape of the
    // cached value changes, and stale entries from the old shape are simply never read again.
    private const string Key = "popularproducts:v1";

    private static readonly DistributedCacheEntryOptions Lifetime =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

    /// <summary>Returns the cached value, computing and storing it on a miss.</summary>
    public Task<string> GetAsync(CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync(
            Key,
            async ct =>
            {
                // TODO: the expensive read this cache exists to avoid — a heavy query, an aggregate,
                // a third-party call. Whatever you return here is what gets stored.
                await Task.CompletedTask.ConfigureAwait(false);
                return "TODO";
            },
            Lifetime,
            cancellationToken);

    /// <summary>
    /// Drops the cached value. Call this from the command handler that changes the underlying data —
    /// invalidating at the point of the write is what keeps the cache from serving a stale answer.
    /// </summary>
    public Task InvalidateAsync(CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(Key, cancellationToken);
}
