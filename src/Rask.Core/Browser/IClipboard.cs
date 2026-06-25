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
