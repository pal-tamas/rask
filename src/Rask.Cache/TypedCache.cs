using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Rask.Caching;

/// <summary>
///     <see cref="ICache" /> over the registered <see cref="IDistributedCache" />, serializing with the app's
///     <see cref="CacheOptions.Json" /> when it set one and with reflection when it did not.
/// </summary>
internal sealed class TypedCache(IDistributedCache store, IOptions<CacheOptions> options) : ICache
{
    private readonly JsonSerializerOptions _json = JsonFor(options.Value.Json);

    // The loads in flight, by key: callers that miss the same key together wait for one load instead of each running it
    // against the database the cache stands in front of. In this process; another instance loads its own.
    private readonly ConcurrentDictionary<string, Task> _loading = new(StringComparer.Ordinal);

    public async Task<T?> Get<T>(string key, CancellationToken cancellationToken = default)
    {
        var bytes = await store.GetAsync(key, cancellationToken).ConfigureAwait(false);
        return bytes is null ? default : JsonSerializer.Deserialize(bytes, TypeInfo<T>());
    }

    public Task Forget(string key, CancellationToken cancellationToken = default) =>
        store.RemoveAsync(key, cancellationToken);

    public async Task<T> Remember<T>(
        string key, Func<CancellationToken, Task<T>> load, CacheLifetime lifetime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(load);
        var typeInfo = TypeInfo<T>();
        var bytes = await store.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (bytes is not null)
        {
            return JsonSerializer.Deserialize(bytes, typeInfo)!;
        }

        var mine = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_loading.GetOrAdd(key, mine.Task) is var flight && !ReferenceEquals(flight, mine.Task))
        {
            return await AwaitOrLoadAsync(flight, () => LoadAsync(key, load, lifetime, typeInfo, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            var loaded = await LoadAsync(key, load, lifetime, typeInfo, cancellationToken).ConfigureAwait(false);
            mine.SetResult(loaded);
            return loaded;
        }
        catch (Exception e)
        {
            mine.SetException(e);
            _ = mine.Task.Exception; // observed: a waiter that sees it loads for itself
            throw;
        }
        finally
        {
            _loading.TryRemove(new KeyValuePair<string, Task>(key, mine.Task));
        }
    }

    // Another caller's load of the same key: its answer, or — when it failed, was cancelled with its caller, or loads
    // another type under the key — one of our own.
    private static async Task<T> AwaitOrLoadAsync<T>(Task flight, Func<Task<T>> loadOwn, CancellationToken cancellationToken)
    {
        if (flight is Task<T> same)
        {
            try
            {
                return await same.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Its load failed; ours may not.
            }
        }

        return await loadOwn().ConfigureAwait(false);
    }

    private async Task<T> LoadAsync<T>(
        string key, Func<CancellationToken, Task<T>> load, CacheLifetime lifetime, JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        var loaded = await load(cancellationToken).ConfigureAwait(false);
        await store.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(loaded, typeInfo), lifetime.ToEntryOptions(), cancellationToken)
            .ConfigureAwait(false);
        return loaded;
    }

    public Task Set<T>(string key, T value, CacheLifetime lifetime, CancellationToken cancellationToken) =>
        store.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, TypeInfo<T>()), lifetime.ToEntryOptions(), cancellationToken);

    private JsonTypeInfo<T> TypeInfo<T>()
    {
        try
        {
            return _json.GetTypeInfo<T>();
        }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"The cache cannot store {typeof(T).Name}: this app is trimmed, so values are serialized from the "
                + $"JsonSerializerContext it registered. Add [JsonSerializable(typeof({typeof(T).Name}))] to that context, "
                + "and register it once with AddRaskCache<AppDbContext>(o => o.Json = AppJson.Default).", e);
        }
    }

    private static JsonSerializerOptions JsonFor(IJsonTypeInfoResolver? registered)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            TypeInfoResolver = registered ?? Reflection(),
        };
        json.MakeReadOnly();
        return json;
    }

    // Only when the app registered no context. In a trimmed app reflection is switched off, the guard reads
    // false, and a type the context lacks fails in TypeInfo with the line to add rather than with a trim warning.
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Guarded by JsonSerializer.IsReflectionEnabledByDefault, which the trimmer turns off.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Guarded by JsonSerializer.IsReflectionEnabledByDefault, which AOT turns off.")]
    private static DefaultJsonTypeInfoResolver? Reflection() =>
        JsonSerializer.IsReflectionEnabledByDefault ? new DefaultJsonTypeInfoResolver() : null;
}
