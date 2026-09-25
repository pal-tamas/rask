using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IVisualViewport" />, backed by the unified <see cref="IJSRuntime" />.
///     <c>window.visualViewport</c> is a live object, so the read goes through the framework's
///     <c>__raskApi.visualViewport</c> helper, which returns a plain snapshot.
/// </summary>
public sealed class VisualViewportReader(IJSRuntime js) : IVisualViewport
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskApi.visualViewportSupported");

    /// <inheritdoc />
    public ValueTask<VisualViewport?> GetAsync() =>
        js.InvokeAsync<VisualViewport?>("__raskApi.visualViewport");
}
