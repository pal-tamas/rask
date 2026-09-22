using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Rask.Cache;

/// <summary>
///     <see cref="ICache" /> over the registered <see cref="IDistributedCache" />, serializing with the app's
///     <see cref="CacheOptions.Json" /> when it set one and with reflection when it did not.
/// </summary>
internal sealed class TypedCache(IDistributedCache store, IOptions<CacheOptions> options) : ICache
{
    private readonly JsonSerializerOptions _json = JsonFor(options.Value.Json);

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
            return (JsonTypeInfo<T>)_json.GetTypeInfo(typeof(T));
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
    private static IJsonTypeInfoResolver? Reflection() =>
        JsonSerializer.IsReflectionEnabledByDefault ? new DefaultJsonTypeInfoResolver() : null;
}
