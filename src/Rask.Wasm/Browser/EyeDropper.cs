using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Typed access to the EyeDropper API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/EyeDropper_API" />) — let the user pick
///     a color from anywhere on screen (a magnifier loupe), e.g. for a design tool or theme editor.
///     <b>WASM-only:</b> <c>EyeDropper.open()</c> needs <em>transient</em> user activation (the same
///     constraint as <see cref="IFullscreen" />), which the Server/WebSocket round-trip loses, so it's
///     registered only by the WASM host.
/// </summary>
/// <remarks>
///     Call from a user-gesture handler and gate on <see cref="IsSupportedAsync" /> (Chromium-family only at
///     the time of writing). Cancelling the picker (Escape) is not an error — <see cref="OpenAsync" /> returns
///     <c>null</c> rather than throwing.
/// </remarks>
public interface IEyeDropper
{
    /// <summary>Whether the browser supports the EyeDropper API (<c>"EyeDropper" in window</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Opens the eyedropper and resolves with the picked color as an sRGB hex string (e.g.
    ///     <c>"#3366ff"</c>), or <c>null</c> if the user cancelled. Must be called from a user-gesture handler.
    /// </summary>
    ValueTask<string?> OpenAsync();
}

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
