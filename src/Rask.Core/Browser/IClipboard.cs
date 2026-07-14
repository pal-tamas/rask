using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the system clipboard's text (the async Clipboard API,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Clipboard" />). Inject it through a
///     component constructor and call from an event handler:
///     <code>
///     await clipboard.WriteTextAsync(code);
///     var pasted = await clipboard.ReadTextAsync();
///     </code>
/// </summary>
/// <remarks>
///     Clipboard access is gated by the browser: it requires a secure context (HTTPS or localhost) and,
///     for reads, a user gesture and/or an explicit permission grant. A blocked call surfaces as a
///     <see cref="JSException" /> from the awaited task — handle it rather than assuming success.
/// </remarks>
public interface IClipboard
{
    /// <summary>Writes <paramref name="text" /> to the clipboard (<c>navigator.clipboard.writeText</c>).</summary>
    ValueTask WriteTextAsync(string text);

    /// <summary>Reads the clipboard's current text (<c>navigator.clipboard.readText</c>).</summary>
    ValueTask<string> ReadTextAsync();
}

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
