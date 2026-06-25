using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     A typed view over one Web Storage area — the browser's <c>localStorage</c> or
///     <c>sessionStorage</c> (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Storage" />).
///     Reach it through <see cref="IBrowserStorage.Local" /> / <see cref="IBrowserStorage.Session" />,
///     which inject the same way on Server (per-session WS-bound runtime) and WASM (in-process bridge).
/// </summary>
/// <remarks>
///     Every member is a thin, awaitable wrapper over <see cref="IJSRuntime" />; each call round-trips
///     to the browser, so prefer reading a value once into a field over calling these in a tight loop.
///     Storage values are strings — serialize your own objects (e.g. with <c>System.Text.Json</c>).
/// </remarks>
public interface IWebStorage
{
    /// <summary>
    ///     Reads the value stored under <paramref name="key" />, or <c>null</c> if the key is absent
    ///     (<c>Storage.getItem</c>).
    /// </summary>
    ValueTask<string?> GetAsync(string key);

    /// <summary>Writes <paramref name="value" /> under <paramref name="key" /> (<c>Storage.setItem</c>).</summary>
    ValueTask SetAsync(string key, string value);

    /// <summary>Removes <paramref name="key" /> if present (<c>Storage.removeItem</c>).</summary>
    ValueTask RemoveAsync(string key);

    /// <summary>Removes every key in this storage area (<c>Storage.clear</c>).</summary>
    ValueTask ClearAsync();

    /// <summary>
    ///     Returns the key at <paramref name="index" /> in this storage area, or <c>null</c> if the
    ///     index is out of range (<c>Storage.key</c>). Iteration order is implementation-defined.
    /// </summary>
    ValueTask<string?> KeyAsync(int index);

    /// <summary>Returns the number of stored keys (<c>Storage.length</c>).</summary>
    ValueTask<int> LengthAsync();
}
