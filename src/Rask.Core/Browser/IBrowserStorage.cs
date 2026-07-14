using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the browser's two Web Storage areas. Inject it through a component constructor
///     (<c>public MyPage(IBrowserStorage storage)</c>) and call from an event handler or lifecycle hook:
///     <code>
///     await storage.Local.SetAsync("theme", "dark");
///     var theme = await storage.Local.GetAsync("theme");
///     </code>
///     Identical on Server and WASM — both resolve to the same <see cref="IWebStorage" /> surface over
///     the unified <c>IJSRuntime</c>.
/// </summary>
public interface IBrowserStorage
{
    /// <summary>Persistent storage that survives across browser sessions (<c>window.localStorage</c>).</summary>
    IWebStorage Local { get; }

    /// <summary>Per-tab storage cleared when the page session ends (<c>window.sessionStorage</c>).</summary>
    IWebStorage Session { get; }
}

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

/// <summary>
///     Default <see cref="IBrowserStorage" />, backed by the unified <see cref="IJSRuntime" />. Registered
///     by both hosts alongside <c>Navigator</c>; you normally inject the interface, not this type.
/// </summary>
public sealed class BrowserStorage : IBrowserStorage
{
    /// <summary>Creates the two storage views over <paramref name="js" />.</summary>
    public BrowserStorage(IJSRuntime js)
    {
        ArgumentNullException.ThrowIfNull(js);
        Local = new WebStorage(js, "localStorage");
        Session = new WebStorage(js, "sessionStorage");
    }

    /// <inheritdoc />
    public IWebStorage Local { get; }

    /// <inheritdoc />
    public IWebStorage Session { get; }

    // One storage area. The store name ("localStorage" / "sessionStorage") is the dotted-path root
    // resolved on `window`, so the same code drives both areas — and both transports — unchanged.
    private sealed class WebStorage(IJSRuntime js, string store) : IWebStorage
    {
        public ValueTask<string?> GetAsync(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return js.InvokeAsync<string?>($"{store}.getItem", key);
        }

        public ValueTask SetAsync(string key, string value)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);
            return js.InvokeVoidAsync($"{store}.setItem", key, value);
        }

        public ValueTask RemoveAsync(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return js.InvokeVoidAsync($"{store}.removeItem", key);
        }

        public ValueTask ClearAsync() => js.InvokeVoidAsync($"{store}.clear");

        public ValueTask<string?> KeyAsync(int index) => js.InvokeAsync<string?>($"{store}.key", index);

        // `length` is a property, not a method. The client resolves a dotted identifier and, when the
        // last segment isn't a function, returns its value as-is — so a plain InvokeAsync reads it.
        public ValueTask<int> LengthAsync() => js.InvokeAsync<int>($"{store}.length");
    }
}
