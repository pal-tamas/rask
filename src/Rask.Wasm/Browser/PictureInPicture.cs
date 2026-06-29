using Microsoft.JSInterop;
using Rask.Core;

namespace Rask.Wasm.Browser;

/// <summary>
///     Typed access to the Picture-in-Picture API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Picture-in-Picture_API" />) — float a
///     <c>&lt;video&gt;</c> out into an always-on-top miniplayer the user keeps visible while they scroll or
///     switch tabs. <b>WASM-only:</b> <c>requestPictureInPicture()</c> needs <em>transient</em> user
///     activation (the same constraint as <see cref="IFullscreen" />), which the Server/WebSocket round-trip
///     loses, so it's registered only by the WASM host.
/// </summary>
/// <remarks>
///     Call <see cref="RequestAsync" /> from a user-gesture handler and gate on <see cref="IsSupportedAsync" />.
///     A denied request (no activation, video not ready, or disabled by attribute/policy) surfaces as a
///     <see cref="JSException" /> — wrap in try/catch.
/// </remarks>
public interface IPictureInPicture
{
    /// <summary>Whether Picture-in-Picture is available and not blocked (<c>document.pictureInPictureEnabled</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>Whether a video is currently in the miniplayer (<c>document.pictureInPictureElement != null</c>).</summary>
    ValueTask<bool> IsActiveAsync();

    /// <summary>
    ///     Requests Picture-in-Picture for the <c>&lt;video&gt;</c> referenced by <paramref name="video" />
    ///     (<c>video.requestPictureInPicture()</c>). Must be called from a user-gesture handler.
    /// </summary>
    ValueTask RequestAsync(ElementRef video);

    /// <summary>Closes the miniplayer (<c>document.exitPictureInPicture()</c>); a no-op when none is open.</summary>
    ValueTask ExitAsync();
}

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
