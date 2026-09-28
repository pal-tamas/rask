using Microsoft.JSInterop;
using Rask.Core;

namespace Rask.Wasm.Browser;

/// <summary>
///     Default <see cref="IPictureInPicture" />, backed by the unified <see cref="IJSRuntime" />. The video is
///     handed across as an <see cref="ElementRef" /> (the JSON reviver resolves it to the live DOM node); the
///     request and not-active-exit shapes go through the framework's <c>__raskPip</c> helper.
/// </summary>
public sealed class PictureInPicture(IJSRuntime js) : IPictureInPicture
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskPip.isSupported");

    /// <inheritdoc />
    public ValueTask<bool> IsActiveAsync() => js.InvokeAsync<bool>("__raskPip.isActive");

    /// <inheritdoc />
    public ValueTask RequestAsync(ElementRef video)
    {
        ArgumentNullException.ThrowIfNull(video);
        return js.InvokeVoidAsync("__raskPip.request", video);
    }

    /// <inheritdoc />
    public ValueTask ExitAsync() => js.InvokeVoidAsync("__raskPip.exit");
}
