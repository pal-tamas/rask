using System.Reflection.Metadata;
using Rask.Core.HotReload;
using Rask.Wasm.HotReload;

namespace Rask.Wasm.Tests.HotReload;

/// <summary>
///     The WASM "applied" announcement. Runs against the non-browser <c>JSInterop</c> stubs, which
///     record the call instead of crossing a JSImport — the same seam the dispatch-shape tests use.
/// </summary>
/// <remarks>
///     The repaint itself is not tested here and does not belong to this class: <c>WasmLiveSession</c>
///     inherits it from <c>LiveSessionBase</c>, and <c>HotReloadPhaseTests</c> covers the coordinator.
///     What is uncovered without this is the last hop — that finishing an apply actually reaches the
///     page, which for WASM is a direct call rather than a pushed frame.
/// </remarks>
public sealed class WasmHotReloadBridgeTests
{
    /// <summary>
    ///     Guards the rest of this class. <see cref="WasmHotReloadBridge.Subscribe" /> early-returns
    ///     unless the feature switch is on, so with it off every assertion below would pass while
    ///     executing nothing — the exact failure mode the csproj's MetadataUpdaterSupport prevents.
    /// </summary>
    [Fact]
    public void The_feature_switch_is_on_in_this_assembly() =>
        Assert.True(
            MetadataUpdater.IsSupported,
            "MetadataUpdaterSupport (csproj) and DOTNET_MODIFIABLE_ASSEMBLIES (hotreload.runsettings) "
            + "must both stay set, or the hot-reload bridge tests pass vacuously.");

    [Fact]
    public void An_applied_reload_reaches_the_page_exactly_once_per_apply()
    {
        JSInterop.ResetHotReloadAppliedCount();
        WasmHotReloadBridge.Subscribe();

        RaskHotReload.RaiseApplied();

        Assert.Equal(1, JSInterop.HotReloadAppliedCount);
    }

    [Fact]
    public void Subscribing_twice_does_not_show_the_indicator_twice()
    {
        // A host that builds more than one app instance in a process must not stack handlers, or one
        // edit would flash the pill once per instance ever created.
        WasmHotReloadBridge.Subscribe();
        WasmHotReloadBridge.Subscribe();
        WasmHotReloadBridge.Subscribe();
        JSInterop.ResetHotReloadAppliedCount();

        RaskHotReload.RaiseApplied();

        Assert.Equal(1, JSInterop.HotReloadAppliedCount);
    }
}
