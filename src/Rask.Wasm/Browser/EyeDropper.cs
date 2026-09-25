using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Default <see cref="IEyeDropper" />, backed by the unified <see cref="IJSRuntime" />. The picker and its
///     cancel-to-<c>null</c> shape go through the framework's <c>__raskEyeDropper</c> helper.
/// </summary>
public sealed class EyeDropper(IJSRuntime js) : IEyeDropper
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskEyeDropper.isSupported");

    /// <inheritdoc />
    public ValueTask<string?> OpenAsync() => js.InvokeAsync<string?>("__raskEyeDropper.open");
}
