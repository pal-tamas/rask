using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Typed access to the Idle Detection API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/IdleDetector" />) — be notified when the
///     user goes idle (no input for a threshold) or the screen locks, e.g. to auto-lock a session, pause a
///     sync, or update presence in a collaborative app. <b>WASM-only:</b> permission must be requested from
///     <em>transient</em> user activation and the detector needs the live document, which the Server/WebSocket
///     round-trip can't carry, so it's registered only by the WASM host.
/// </summary>
/// <remarks>
///     <para>
///         Gated on the <c>idle-detection</c> permission: call <see cref="RequestPermissionAsync" /> from a
///         user-gesture handler first, and only <see cref="WatchAsync" /> when it returns <c>"granted"</c>.
///         The browser <b>pushes</b> each change to the C# callback (via a static <c>[JSInvokable]</c>). Watch
///         from a lifecycle hook and dispose the returned handle on unmount; a callback that updates state
///         should call <c>StateHasChanged()</c> (it's a subscription, not a render/binding callback, so
///         RASK026 doesn't apply).
///     </para>
/// </remarks>
public interface IIdleDetector
{
    /// <summary>Whether the browser supports the Idle Detection API (<c>"IdleDetector" in window</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Requests the <c>idle-detection</c> permission, returning the resulting state
    ///     (<c>"granted"</c> / <c>"denied"</c>). Must be called from a user-gesture handler.
    /// </summary>
    ValueTask<string> RequestPermissionAsync();

    /// <summary>
    ///     Starts detection and invokes <paramref name="onChange" /> with an <see cref="IdleReading" /> on each
    ///     user/screen state change. <paramref name="thresholdSeconds" /> is the idle threshold (the spec
    ///     enforces a 60-second minimum). Dispose the returned handle to stop. Throws if permission was not
    ///     granted.
    /// </summary>
    ValueTask<IAsyncDisposable> WatchAsync(Func<IdleReading, Task> onChange, int thresholdSeconds = 60);
}
