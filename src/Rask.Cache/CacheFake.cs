using Rask.Batteries;

namespace Rask.Cache;

/// <summary>A test's stand-in for the cache: <c>using var cache = Cache.Fake();</c>.</summary>
public static class CacheFakes
{
    extension(Cache)
    {
        /// <summary>
        ///     Takes the place of the cache for this test — an in-memory one that really stores, so the
        ///     code under test behaves as it would in production — until the returned fake is disposed:
        /// </summary>
        /// <remarks>
        ///     <code>
        ///     using var cache = Cache.Fake();
        ///
        ///     await page.Visit("/products");
        ///     await page.Visit("/products");
        ///
        ///     cache.Loaded("products").Once();   // the second visit was served from the cache
        ///     </code>
        ///     <para>
        ///         Expiry is real and reads the app's clock, so <c>Clock.Fake</c> plus <c>Advance</c> proves
        ///         a value is reloaded once it is stale. Scoped to the test's own flow, so tests running in
        ///         parallel never see each other's keys. It stands in front of <c>Cache.Remember</c>; a
        ///         class that takes <see cref="ICache" /> in its constructor is handed whatever the
        ///         container holds, so register the fake there too —
        ///         <c>services.AddSingleton&lt;ICache&gt;(cache)</c> — when the code under test injects it.
        ///     </para>
        /// </remarks>
        public static CacheFake Fake() => new();
    }
}

/// <summary>An in-memory cache that remembers what a test asked of it.</summary>
public sealed class CacheFake : ICache, IDisposable
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<Touch> _touches = [];
    private readonly ICache? _previous;
    private readonly Lock _gate = new();

    internal CacheFake()
    {
        _previous = Cache.Faked.Value;
        Cache.Faked.Value = this;
    }

    /// <summary>
    ///     The keys whose value had to be <em>loaded</em> — a miss, or a value that had expired. A key
    ///     remembered twice but loaded once is the whole point of a cache, so this is what to assert on:
    ///     <c>cache.Loaded("products").Once()</c>.
    /// </summary>
    public Counting<string> Loaded() => Counting(TouchKind.Loaded, "load");

    /// <inheritdoc cref="Loaded()" />
    /// <param name="key">The key to ask about.</param>
    public Counting<string> Loaded(string key) => Loaded().Where(k => k == key, $"of \"{key}\"");

    /// <summary>The keys a <c>Remember</c> or <c>Get</c> asked for, hit or miss.</summary>
    public Counting<string> Read() => Counting(TouchKind.Read, "read");

    /// <inheritdoc cref="Read()" />
    /// <param name="key">The key to ask about.</param>
    public Counting<string> Read(string key) => Read().Where(k => k == key, $"of \"{key}\"");

    /// <summary>The keys <c>Forget</c> removed.</summary>
    public Counting<string> Forgotten() => Counting(TouchKind.Forgotten, "forget");

    /// <inheritdoc cref="Forgotten()" />
    /// <param name="key">The key to ask about.</param>
    public Counting<string> Forgotten(string key) => Forgotten().Where(k => k == key, $"of \"{key}\"");

    /// <summary>Empties it and forgets what was asked of it, without putting the real cache back.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _touches.Clear();
        }
    }

    /// <summary>Puts the real cache back.</summary>
    public void Dispose() => Cache.Faked.Value = _previous;

    /// <inheritdoc />
    public Task<T?> Get<T>(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _touches.Add(new Touch(TouchKind.Read, key));
            return Task.FromResult(Live(key) is { } entry ? (T?)entry.Value : default);
        }
    }

    /// <inheritdoc />
    public Task Forget(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _touches.Add(new Touch(TouchKind.Forgotten, key));
            _entries.Remove(key);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<T> Remember<T>(
        string key, Func<CancellationToken, Task<T>> load, CacheLifetime lifetime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(load);
        lock (_gate)
        {
            _touches.Add(new Touch(TouchKind.Read, key));
            if (Live(key) is { } hit)
            {
                // A sliding window is kept alive by being read, exactly as the real cache does.
                if (hit.Sliding is { } window)
                {
                    _entries[key] = hit with { Expires = Clock.Now + window };
                }

                return (T)hit.Value!;
            }

            _touches.Add(new Touch(TouchKind.Loaded, key));
        }

        var value = await load(cancellationToken).ConfigureAwait(false);
        await ((ICache)this).Set(key, value, lifetime, cancellationToken).ConfigureAwait(false);
        return value;
    }

    /// <inheritdoc />
    public Task Set<T>(string key, T value, CacheLifetime lifetime, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _entries[key] = new Entry(value, Expiry(lifetime), lifetime.Sliding);
        }

        return Task.CompletedTask;
    }

    private static DateTimeOffset? Expiry(CacheLifetime lifetime) =>
        lifetime.For is { } span ? Clock.Now + span
        : lifetime.Sliding is { } window ? Clock.Now + window
        : lifetime.Until;

    private Counting<string> Counting(TouchKind kind, string verb) =>
        new([.. _touches.Where(t => t.Kind == kind).Select(t => t.Key)], "key", verb, static k => $"\"{k}\"");

    /// <summary>The entry under <paramref name="key" /> if it has not expired; expiry is checked on read.</summary>
    private Entry? Live(string key)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            return null;
        }

        if (entry.Expires is { } expires && expires <= Clock.Now)
        {
            _entries.Remove(key);
            return null;
        }

        return entry;
    }

    private enum TouchKind
    {
        Read,
        Loaded,
        Forgotten,
    }

    private sealed record Entry(object? Value, DateTimeOffset? Expires, TimeSpan? Sliding);

    private sealed record Touch(TouchKind Kind, string Key);
}
