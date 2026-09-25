using Microsoft.JSInterop;
using Rask.Core;

namespace Rask.Wasm.Browser;

/// <summary>
///     Typed access to the Fullscreen API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Fullscreen_API" />) — present an
///     element (or the whole page) fullscreen, e.g. for media, games, or an immersive view. Pairs with
///     <see cref="IScreenOrientation" />: <c>LockAsync</c> generally requires fullscreen, so request
///     fullscreen first, then lock. <b>WASM-only:</b> <c>requestFullscreen()</c> needs <em>transient</em>
///     user activation (the same constraint as <see cref="IShare" />), which the Server/WebSocket
///     round-trip loses, so it's registered only by the WASM host.
/// </summary>
/// <remarks>
///     Requires a secure context and a user-gesture handler. A denied request (no activation, or blocked
///     by permissions policy) surfaces as a <see cref="JSException" /> from <see cref="RequestAsync" /> —
///     gate on <see cref="IsSupportedAsync" /> and wrap in try/catch.
/// </remarks>
public interface IFullscreen
{
    /// <summary>Whether fullscreen is available and not blocked (<c>document.fullscreenEnabled</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>Whether something is currently fullscreen (<c>document.fullscreenElement != null</c>).</summary>
    ValueTask<bool> IsActiveAsync();

    /// <summary>
    ///     Requests fullscreen for <paramref name="element" /> (<c>element.requestFullscreen()</c>), or the
    ///     whole page when it is <c>null</c>. Must be called from a user-gesture handler.
    /// </summary>
    ValueTask RequestAsync(ElementRef? element = null);

    /// <summary>Exits fullscreen (<c>document.exitFullscreen()</c>); a no-op when not fullscreen.</summary>
    ValueTask ExitAsync();
}
