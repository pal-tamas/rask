using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Typed access to the Screen Orientation API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Screen_Orientation_API" />) — read the
///     current orientation and, for an installed/fullscreen app, lock it. <b>WASM-only:</b> locking needs
///     the live document (and usually fullscreen), state the Server/WebSocket transport can't carry, so
///     it's registered only by the WASM host.
/// </summary>
/// <remarks>
///     Requires a secure context. <see cref="LockAsync" /> rejects unless the document is fullscreen on
///     most browsers, and on desktop it's frequently unsupported — gate on <see cref="IsSupportedAsync" />
///     and wrap in try/catch; a rejection surfaces as a <see cref="JSException" />.
/// </remarks>
public interface IScreenOrientation
{
    /// <summary>Whether the browser exposes screen orientation (<c>"orientation" in screen</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>Reads the current orientation type and angle (<c>screen.orientation</c>).</summary>
    ValueTask<OrientationInfo> GetAsync();

    /// <summary>
    ///     Locks the screen to <paramref name="orientation" /> (<c>screen.orientation.lock</c>). Usually
    ///     requires fullscreen; rejects with a <see cref="JSException" /> when not permitted.
    /// </summary>
    ValueTask LockAsync(OrientationLock orientation);

    /// <summary>Releases any orientation lock (<c>screen.orientation.unlock</c>).</summary>
    ValueTask UnlockAsync();
}
