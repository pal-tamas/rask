using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IViewTransitions" />, backed by the unified <see cref="IJSRuntime" /> and the
///     shared <c>__raskVt</c> helper both client runtimes splice in.
/// </summary>
public sealed class ViewTransitions(IJSRuntime js) : IViewTransitions
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskVt.supported");

    /// <inheritdoc />
    public ValueTask<bool> SetEnabledAsync(bool enabled) => js.InvokeAsync<bool>("__raskVt.set", enabled);

    /// <inheritdoc />
    public ValueTask<bool> IsActiveAsync() => js.InvokeAsync<bool>("__raskVt.active");
}
