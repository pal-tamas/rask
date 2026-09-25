namespace Rask.Caching;

/// <summary>The lifetime steps on an injected <see cref="ICache" />, worded as on <see cref="Cache" />.</summary>
public static class CacheExtensions
{
    extension(ICache cache)
    {
        /// <summary>The value under <paramref name="key" />, or what <paramref name="load" /> returns, which is stored.</summary>
        public Remembering<T> Remember<T>(string key, Func<Task<T>> load, CancellationToken cancellationToken = default) =>
            new(Checked(cache), Cache.Key(key), Cache.Loader(load), default, cancellationToken);

        /// <inheritdoc cref="Remember{T}(ICache, string, Func{Task{T}}, CancellationToken)" />
        public Remembering<T> Remember<T>(string key, Func<CancellationToken, Task<T>> load, CancellationToken cancellationToken = default) =>
            new(Checked(cache), Cache.Key(key), load ?? throw new ArgumentNullException(nameof(load)), default, cancellationToken);

        /// <inheritdoc cref="Remember{T}(ICache, string, Func{Task{T}}, CancellationToken)" />
        public Remembering<T> Remember<T>(string key, Func<T> load, CancellationToken cancellationToken = default) =>
            new(Checked(cache), Cache.Key(key), Cache.Loader(load), default, cancellationToken);

        /// <summary>Stores <paramref name="value" /> under <paramref name="key" />.</summary>
        public Setting<T> Set<T>(string key, T value, CancellationToken cancellationToken = default) =>
            new(Checked(cache), Cache.Key(key), value, default, cancellationToken);
    }

    private static ICache Checked(ICache cache) => cache ?? throw new ArgumentNullException(nameof(cache));
}
