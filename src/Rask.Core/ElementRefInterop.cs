using Microsoft.JSInterop;

namespace Rask.Core;

/// <summary>
///     Built-in element-ref operations over <see cref="IJSRuntime" />. Each passes the ref to a
///     framework JS helper (<c>__raskEl.*</c>) that receives the resolved DOM element.
/// </summary>
public static class ElementRefInterop
{
    /// <summary>Focus the element.</summary>
    public static ValueTask FocusAsync(this ElementRef element, IJSRuntime js) =>
        js.InvokeVoidAsync("__raskEl.focus", element);

    /// <summary>Remove focus from the element.</summary>
    public static ValueTask BlurAsync(this ElementRef element, IJSRuntime js) =>
        js.InvokeVoidAsync("__raskEl.blur", element);

    /// <summary>Scroll the element into view (smooth, nearest).</summary>
    public static ValueTask ScrollIntoViewAsync(this ElementRef element, IJSRuntime js) =>
        js.InvokeVoidAsync("__raskEl.scrollIntoView", element);
}
