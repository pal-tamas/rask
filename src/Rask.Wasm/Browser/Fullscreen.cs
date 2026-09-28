using Microsoft.JSInterop;
using Rask.Core;

namespace Rask.Wasm.Browser;

/// <summary>
///     Default <see cref="IFullscreen" />, backed by the unified <see cref="IJSRuntime" />. The element is
///     handed across as an <see cref="ElementRef" /> (the JSON reviver resolves it to the live DOM node);
///     the optional-target and not-fullscreen-exit shapes go through the framework's <c>__raskFullscreen</c>
///     helper.
/// </summary>
public sealed class Fullscreen(IJSRuntime js) : IFullscreen
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskFullscreen.isSupported");

    /// <inheritdoc />
    public ValueTask<bool> IsActiveAsync() => js.InvokeAsync<bool>("__raskFullscreen.isActive");

    /// <inheritdoc />
    public ValueTask RequestAsync(ElementRef? element = null) =>
        js.InvokeVoidAsync("__raskFullscreen.request", element);

    /// <inheritdoc />
    public ValueTask ExitAsync() => js.InvokeVoidAsync("__raskFullscreen.exit");
}
