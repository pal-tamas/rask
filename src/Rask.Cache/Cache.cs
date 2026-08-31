using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Caching.Distributed;

namespace Rask.Cache;

/// <summary>
/// The default <see cref="ICache"/>: serializes values to JSON and stores the bytes through any
/// <see cref="IDistributedCache"/> (in a Rask app, <see cref="RaskDistributedCache{TContext}"/>).
/// </summary>
public sealed class Cache(IDistributedCache cache) : ICache
{
    internal const string TrimWarning =
        "The typed cache serializes T with reflection-based System.Text.Json. Use the JsonTypeInfo<T> overloads in a trimmed or AOT app.";

    /// <inheritdoc/>
    [RequiresUnreferencedCode(TrimWarning)]
    [RequiresDynamicCode(TrimWarning)]
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var bytes = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes);
    }

    /// <inheritdoc/>
    public async Task<T?> GetAsync<T>(string key, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var bytes = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        return bytes is null ? default : JsonSerializer.Deserialize(bytes, typeInfo);
    }

    /// <inheritdoc/>
    [RequiresUnreferencedCode(TrimWarning)]
    [RequiresDynamicCode(TrimWarning)]
    public Task SetAsync<T>(string key, T value, DistributedCacheEntryOptions? options = null, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return cache.SetAsync(key, bytes, options ?? new DistributedCacheEntryOptions(), cancellationToken);
    }

    /// <inheritdoc/>
    public Task SetAsync<T>(string key, T value, JsonTypeInfo<T> typeInfo, DistributedCacheEntryOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        return cache.SetAsync(key, bytes, options ?? new DistributedCacheEntryOptions(), cancellationToken);
    }

    /// <inheritdoc/>
    [RequiresUnreferencedCode(TrimWarning)]
    [RequiresDynamicCode(TrimWarning)]
    public async Task<T> GetOrAddAsync<T>(string key, Func<CancellationToken, Task<T>> factory, DistributedCacheEntryOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var bytes = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (bytes is not null)
        {
            return JsonSerializer.Deserialize<T>(bytes)!;
        }

        var created = await factory(cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.SerializeToUtf8Bytes(created);
        await cache.SetAsync(key, payload, options ?? new DistributedCacheEntryOptions(), cancellationToken).ConfigureAwait(false);
        return created;
    }

    /// <inheritdoc/>
    public async Task<T> GetOrAddAsync<T>(string key, Func<CancellationToken, Task<T>> factory, JsonTypeInfo<T> typeInfo, DistributedCacheEntryOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(typeInfo);
        var bytes = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (bytes is not null)
        {
            return JsonSerializer.Deserialize(bytes, typeInfo)!;
        }

        var created = await factory(cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.SerializeToUtf8Bytes(created, typeInfo);
        await cache.SetAsync(key, payload, options ?? new DistributedCacheEntryOptions(), cancellationToken).ConfigureAwait(false);
        return created;
    }

    /// <inheritdoc/>
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        cache.RemoveAsync(key, cancellationToken);
}
