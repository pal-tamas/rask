using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the Screen Wake Lock API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Screen_Wake_Lock_API" />) — keep the
///     screen from dimming/locking during reading, a timer, navigation, or media playback. Works on both
///     hosts; the browser auto-releases the lock when the page is hidden, and the framework helper
///     re-acquires it when the page becomes visible again.
/// </summary>
/// <remarks>
///     Requires a secure context. The browser releases the lock whenever the page is hidden; the framework
///     helper re-acquires held locks when the page becomes visible again, so a sentinel stays effective
///     across tab switches until you dispose it. An unsupported browser or denied request surfaces as a
///     <see cref="JSException" /> from <see cref="RequestAsync" /> — gate on <see cref="IsSupportedAsync" />
///     and wrap in try/catch.
/// </remarks>
public interface IWakeLock
{
    /// <summary>Whether the browser supports screen wake locks (<c>"wakeLock" in navigator</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Acquires a screen wake lock (<c>navigator.wakeLock.request("screen")</c>) and returns the
    ///     <see cref="IWakeLockSentinel" /> that holds it. Dispose the sentinel to release.
    /// </summary>
    ValueTask<IWakeLockSentinel> RequestAsync();
}
