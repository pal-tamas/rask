using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cache;

/// <summary>
///     The app's cache, with nothing injected — from a handler, a render, a request, a job:
/// </summary>
/// <remarks>
///     <code>
///     var products = await Cache.Remember("products", LoadProducts).For(10.Minutes);
///     await Cache.Set("banner", text).Until(midnight);
///     var banner = await Cache.Get&lt;string&gt;("banner");
///     await Cache.Forget("products");
///     </code>
///     <para>
///         Each call reaches the <see cref="ICache" /> of the work it runs in and is cancelled with that work.
///         Outside any — a hosted service, a timer started at boot — it throws; inject <see cref="ICache" />
///         there instead.
///     </para>
/// </remarks>
public static class Cache
{
    /// <summary>The value under <paramref name="key" />, or what <paramref name="load" /> returns, which is stored.</summary>
    public static Remembering<T> Remember<T>(string key, Func<Task<T>> load, CancellationToken cancellationToken = default) =>
        new(null, Key(key), Loader(load), default, cancellationToken);

    /// <inheritdoc cref="Remember{T}(string, Func{Task{T}}, CancellationToken)" />
    public static Remembering<T> Remember<T>(string key, Func<CancellationToken, Task<T>> load, CancellationToken cancellationToken = default) =>
        new(null, Key(key), load ?? throw new ArgumentNullException(nameof(load)), default, cancellationToken);

    /// <inheritdoc cref="Remember{T}(string, Func{Task{T}}, CancellationToken)" />
    public static Remembering<T> Remember<T>(string key, Func<T> load, CancellationToken cancellationToken = default) =>
        new(null, Key(key), Loader(load), default, cancellationToken);

    /// <summary>Stores <paramref name="value" /> under <paramref name="key" />.</summary>
    public static Setting<T> Set<T>(string key, T value, CancellationToken cancellationToken = default) =>
        new(null, Key(key), value, default, cancellationToken);

    /// <summary>The value stored under <paramref name="key" />, or <c>default</c> when there is none.</summary>
    public static Task<T?> Get<T>(string key, CancellationToken cancellationToken = default) =>
        Resolve().Get<T>(Key(key), Ambient.Or(cancellationToken));

    /// <summary>Removes <paramref name="key" />, so the next <c>Remember</c> loads it afresh.</summary>
    public static Task Forget(string key, CancellationToken cancellationToken = default) =>
        Resolve().Forget(Key(key), Ambient.Or(cancellationToken));

    internal static ICache Resolve()
    {
        var services = Ambient.Services
            ?? throw new InvalidOperationException(
                "Cache was called outside any work in progress — a handler, a render, a request or a job — so "
                + "there is no app to reach. Inject ICache in the constructor there instead.");

        return services.GetService<ICache>()
            ?? throw new InvalidOperationException(
                "Cache needs Rask.Cache registered: call builder.Services.AddRaskCache<AppDbContext>().");
    }

    internal static Func<CancellationToken, Task<T>> Loader<T>(Func<Task<T>> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        return _ => load();
    }

    internal static Func<CancellationToken, Task<T>> Loader<T>(Func<T> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        return _ => Task.FromResult(load());
    }

    internal static string Key(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return key;
    }
}

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
