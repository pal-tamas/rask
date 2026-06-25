using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IClipboard" />, backed by the unified <see cref="IJSRuntime" />.
///     <c>writeText</c>/<c>readText</c> already return Promises, so no framework JS helper is needed.
/// </summary>
public sealed class Clipboard(IJSRuntime js) : IClipboard
{
    /// <inheritdoc />
    public ValueTask WriteTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return js.InvokeVoidAsync("navigator.clipboard.writeText", text);
    }

    /// <inheritdoc />
    public ValueTask<string> ReadTextAsync() => js.InvokeAsync<string>("navigator.clipboard.readText");
}
