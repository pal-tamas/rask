using System.Reflection.Metadata;
using Rask.Core.Diagnostics;
using Rask.Core.HotReload;

namespace Rask.Wasm.HotReload;

/// <summary>
///     Announces an applied hot reload to the page, the WASM way.
/// </summary>
/// <remarks>
///     <para>
///         The repaint itself needs nothing from this class: <c>WasmLiveSession</c> derives from
///         <c>LiveSessionBase</c>, so it already registers with the coordinator and already re-renders
///         through <c>JSInterop.ApplyRender</c>. All that is missing is the indicator, and a WASM app
///         has no Rask server to push it the Server's out-of-band <c>hotReload</c> frame — so it calls
///         a JS export directly instead.
///     </para>
///     <para>
///         Costs a published app nothing. <see cref="MetadataUpdater.IsSupported" /> is false in a
///         trimmed bundle (trimming folds the feature switch), which is also exactly why hot reload
///         needs the untrimmed build output in the first place.
///     </para>
/// </remarks>
internal static class WasmHotReloadBridge
{
    private static bool _subscribed;

    /// <summary>
    ///     Idempotent: a host that builds more than one app instance in a process must not stack
    ///     handlers, or one edit would show the pill several times.
    /// </summary>
    internal static void Subscribe()
    {
        if (_subscribed || !MetadataUpdater.IsSupported)
        {
            return;
        }

        _subscribed = true;
        RaskHotReload.Applied += OnApplied;
    }

    private static void OnApplied()
    {
        try
        {
            JSInterop.HotReloadApplied();
        }
        catch (Exception ex)
        {
            // Never let the indicator take the app down: this runs on the coordinator's completion
            // path, and an exception escaping here would surface as a failed hot reload rather than
            // as the cosmetic problem it is.
            RaskDiagnostics.Report(
                RaskLogLevel.Warning, "Rask.HotReload", "Rask: hot-reload indicator failed", ex);
        }
    }
}
