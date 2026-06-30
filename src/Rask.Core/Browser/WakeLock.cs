using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     A held screen wake lock (a <c>WakeLockSentinel</c>,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/WakeLockSentinel" />). Keep the
///     reference for as long as the screen should stay awake, then release it by disposing — ideally with
///     <c>await using</c> or from a component's <c>DisposeAsync</c>.
/// </summary>
public interface IWakeLockSentinel : IAsyncDisposable
{
    // Release is DisposeAsync — the sentinel is the lifetime. Disposing twice is a no-op.
}

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

/// <summary>
///     Default <see cref="IWakeLock" />, backed by the unified <see cref="IJSRuntime" />. A
///     <c>WakeLockSentinel</c> is a live JS object <see cref="IJSRuntime" /> can't hand back, so the
///     framework's <c>__raskWakeLock</c> helper keeps it in a small registry and returns an integer id;
///     the sentinel releases by id on dispose.
/// </summary>
public sealed class WakeLock(IJSRuntime js) : IWakeLock
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskWakeLock.isSupported");

    /// <inheritdoc />
    public async ValueTask<IWakeLockSentinel> RequestAsync()
    {
        var id = await js.InvokeAsync<int>("__raskWakeLock.request");
        return new Sentinel(js, id);
    }

    private sealed class Sentinel(IJSRuntime js, int id) : IWakeLockSentinel
    {
        private bool _released;

        public ValueTask DisposeAsync()
        {
            if (_released)
            {
                return ValueTask.CompletedTask;
            }

            _released = true;
            return js.InvokeVoidAsync("__raskWakeLock.release", id);
        }
    }
}
