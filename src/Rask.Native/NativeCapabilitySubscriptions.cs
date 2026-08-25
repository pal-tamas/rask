using System.Collections.Concurrent;

namespace Rask.Native;

/// <summary>
///     The live subscriptions a page has opened over the capability bridge — a geolocation watch, a battery
///     watch, the sensor streams, speech recognition, and a held wake lock.
/// </summary>
/// <remarks>
///     <para>
///         Six of the thirty-five members across the fifteen backends are not request/response: they hand
///         back an <c>IAsyncDisposable</c> and then push. A JSON envelope cannot carry a callback, so the
///         handle stays here on the native side and the page gets an <b>id</b> for it. Starting one is an
///         ordinary invoke whose result is that id; ending one is an invoke carrying it back.
///     </para>
///     <para>
///         Note what does <em>not</em> change: the readings still reach the app's C# through the page's own
///         <c>DotNet.invokeMethodAsync</c> push path, exactly as the web implementation delivers them. The
///         bridge replaces where a reading comes from, not how it gets home — which is why the app half
///         needs no native-specific code.
///     </para>
/// </remarks>
internal sealed class NativeCapabilitySubscriptions : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, IAsyncDisposable> _live = new(StringComparer.Ordinal);

    /// <summary>
    ///     Keep a handle under the id the page chose for it. If that id is somehow already in use, the older
    ///     handle is released rather than leaked — a page reusing an id means it has stopped listening to the
    ///     first stream, and an orphaned GPS watch is a battery complaint with no visible cause.
    /// </summary>
    public void Add(string id, IAsyncDisposable handle)
    {
        if (_live.TryRemove(id, out var previous))
        {
            _ = previous.DisposeAsync();
        }

        _live[id] = handle;
    }

    /// <summary>
    ///     End a subscription. Unknown ids are ignored rather than thrown: a page that reloads mid-stream
    ///     will release handles this side has already dropped, and that is not a fault worth surfacing.
    /// </summary>
    public async ValueTask ReleaseAsync(string? id)
    {
        if (!string.IsNullOrEmpty(id) && _live.TryRemove(id, out var handle))
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Release everything. The app owns the sensors it started, and a GPS watch or a wake lock that
    ///     outlived its app is a battery complaint with no visible cause.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var id in _live.Keys)
        {
            if (_live.TryRemove(id, out var handle))
            {
                try
                {
                    await handle.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // One backend refusing to let go must not strand the rest.
                    Core.Diagnostics.RaskDiagnostics.Report(
                        Core.Diagnostics.RaskLogLevel.Warning, "Rask.Native",
                        "[Rask.Native] a capability subscription threw while being released", ex);
                }
            }
        }
    }
}
