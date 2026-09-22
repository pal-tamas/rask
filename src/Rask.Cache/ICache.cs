using System.ComponentModel;

namespace Rask.Cache;

/// <summary>
///     A typed cache over the app's <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache" />.
///     Reach it with nothing injected through <see cref="Cache" />, or inject this where there is no work in
///     progress to reach it through — a hosted service, a timer started at boot.
/// </summary>
/// <remarks>
///     <code>
///     var products = await cache.Remember("products", LoadProducts).For(10.Minutes);
///     await cache.Set("banner", text).Until(midnight);
///     var banner = await cache.Get&lt;string&gt;("banner");
///     await cache.Forget("products");
///     </code>
///     <para>
///         Values are stored as JSON. A trimmed or AOT app registers its <c>JsonSerializerContext</c> once —
///         <c>AddRaskCache&lt;AppDb&gt;(o =&gt; o.Json = AppJson.Default)</c> — and every call site stays as it is.
///     </para>
///     <para>
///         <c>Remember</c> and <c>Set</c> are extensions that hand back the lifetime steps; the two members
///         below are what they call, and what a test double implements.
///     </para>
/// </remarks>
public interface ICache
{
    /// <summary>The value stored under <paramref name="key" />, or <c>default</c> when there is none.</summary>
    Task<T?> Get<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>Removes <paramref name="key" />, so the next <c>Remember</c> loads it afresh.</summary>
    Task Forget(string key, CancellationToken cancellationToken = default);

    /// <summary>
    ///     The value under <paramref name="key" />, or what <paramref name="load" /> returns — stored for
    ///     <paramref name="lifetime" /> — when there is none. Call <c>Remember(key, load)</c> instead.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Task<T> Remember<T>(string key, Func<CancellationToken, Task<T>> load, CacheLifetime lifetime, CancellationToken cancellationToken);

    /// <summary>Stores <paramref name="value" /> for <paramref name="lifetime" />. Call <c>Set(key, value)</c> instead.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Task Set<T>(string key, T value, CacheLifetime lifetime, CancellationToken cancellationToken);
}
