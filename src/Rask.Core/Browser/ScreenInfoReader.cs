using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IScreenInfo" />, backed by the unified <see cref="IJSRuntime" />. The read goes
///     through the framework's <c>__raskApi.screen</c> helper, which returns a plain snapshot object (the
///     properties live on <c>screen</c> and <c>window</c>, so a single call keeps them consistent).
/// </summary>
public sealed class ScreenInfoReader(IJSRuntime js) : IScreenInfo
{
    /// <inheritdoc />
    public ValueTask<ScreenInfo> GetAsync() => js.InvokeAsync<ScreenInfo>("__raskApi.screen");
}
