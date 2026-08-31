using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Caching.Distributed;

namespace Rask.Cache;

/// <summary>
/// A typed convenience layer over <see cref="IDistributedCache"/>: store and fetch objects (JSON-serialized) by
/// key, with a read-through <see cref="GetOrAddAsync{T}(string, Func{CancellationToken, Task{T}}, DistributedCacheEntryOptions?, CancellationToken)"/>.
/// The reflection-based overloads are the ergonomic default; the <see cref="JsonTypeInfo{T}"/> overloads are
/// trim-/AOT-safe (supply a source-generated <c>JsonSerializerContext</c>).
/// </summary>
public interface ICache
{
    /// <summary>Gets a cached value, or <c>default</c> if the key is missing or expired.</summary>
    [RequiresUnreferencedCode(Cache.TrimWarning)]
    [RequiresDynamicCode(Cache.TrimWarning)]
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>Gets a cached value using a source-generated <paramref name="typeInfo"/> (trim-/AOT-safe).</summary>
    Task<T?> GetAsync<T>(string key, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default);

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/> with the given expiration options (default: no expiration).</summary>
    [RequiresUnreferencedCode(Cache.TrimWarning)]
    [RequiresDynamicCode(Cache.TrimWarning)]
    Task SetAsync<T>(string key, T value, DistributedCacheEntryOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Stores <paramref name="value"/> using a source-generated <paramref name="typeInfo"/> (trim-/AOT-safe).</summary>
    Task SetAsync<T>(string key, T value, JsonTypeInfo<T> typeInfo, DistributedCacheEntryOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or invokes <paramref name="factory"/> on a miss, stores
    /// its result under <paramref name="key"/>, and returns it. This is not a lock: two callers that miss the same
    /// cold key concurrently may both run <paramref name="factory"/>, so keep it idempotent.
    /// </summary>
    [RequiresUnreferencedCode(Cache.TrimWarning)]
    [RequiresDynamicCode(Cache.TrimWarning)]
    Task<T> GetOrAddAsync<T>(string key, Func<CancellationToken, Task<T>> factory, DistributedCacheEntryOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Read-through cache using a source-generated <paramref name="typeInfo"/> (trim-/AOT-safe).</summary>
    Task<T> GetOrAddAsync<T>(string key, Func<CancellationToken, Task<T>> factory, JsonTypeInfo<T> typeInfo, DistributedCacheEntryOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Removes the entry for <paramref name="key"/> if present.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
