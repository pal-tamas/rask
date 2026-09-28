using Microsoft.JSInterop;

namespace Rask.Core.Browser;

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
