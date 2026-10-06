using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Caching;

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

    /// <summary>
    ///     Whether Rask.Cache is registered at all. For an operator surface that renders "off" rather than
    ///     failing — <c>Rask.Dashboard</c> does exactly that. An app should not branch on this: a call with
    ///     nothing registered throws and names the registration that fixes it.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool IsOn =>
        Faked.Value is not null || Ambient.Services?.GetService<ICache>() is not null;

    /// <summary>What <c>Cache.Fake()</c> put in the way of the real cache, for this test's flow alone.</summary>
    internal static readonly AsyncLocal<ICache?> Faked = new();

    internal static ICache Resolve()
    {
        if (Faked.Value is { } fake)
        {
            return fake;
        }

        return Ambient.Reach<ICache>("Cache", "Program.cs says c.Cache.Off()", "AddRaskCache<AppDbContext>()");
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
