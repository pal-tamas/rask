using System.Reflection.Metadata;
using Rask.Core.HotReload;
using Rask.Native.HotReload;
using Rask.Native.Tests.Infrastructure;

namespace Rask.Native.Tests.HotReload;

/// <summary>
///     The native "applied" announcement over the WebView bridge.
/// </summary>
/// <remarks>
///     <para>
///         <b>This does not test native hot reload, which does not work</b> — there is no device-side
///         delta agent and <c>dotnet watch</c> cannot drive a simulator, so nothing ever raises the
///         event on a device (#565). What it tests is the half that is wired anyway: if a delta ever
///         did arrive, the indicator reaches the page over the same bridge everything else uses.
///     </para>
///     <para>
///         Worth having despite that, because the cost of it silently rotting is a future device
///         channel landing on a broken indicator — and because it pins the fire-and-forget contract:
///         the coordinator's completion path must not block on a WebView round trip.
///     </para>
/// </remarks>
public sealed class NativeHotReloadBridgeTests
{
    [Fact]
    public void The_feature_switch_is_on_in_this_assembly() =>
        Assert.True(
            MetadataUpdater.IsSupported,
            "MetadataUpdaterSupport (csproj) and DOTNET_MODIFIABLE_ASSEMBLIES (hotreload.runsettings) "
            + "must both stay set, or NativeHotReloadBridge.Attach early-returns and these pass vacuously.");

    [Fact]
    public async Task An_applied_reload_shows_the_indicator_over_the_bridge()
    {
        var webView = new FakeNativeWebView();
        NativeHotReloadBridge.Attach(webView);

        RaskHotReload.RaiseApplied();

        // Fire-and-forget by design, so poll rather than assume the continuation already ran.
        await WaitForAsync(() => webView.Evaluated.Count > 0);

        Assert.Contains(webView.Evaluated, js => js.Contains("__raskHotReloadPill", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_indicator_follows_the_newest_web_view()
    {
        // Attach is re-pointable: a host that recreates its session must not keep announcing into the
        // WebView it just replaced, and must not stack a second handler either.
        var stale = new FakeNativeWebView();
        var live = new FakeNativeWebView();
        NativeHotReloadBridge.Attach(stale);
        NativeHotReloadBridge.Attach(live);
        stale.Evaluated.Clear();

        RaskHotReload.RaiseApplied();
        await WaitForAsync(() => live.Evaluated.Count > 0);

        Assert.Single(live.Evaluated);
        Assert.Empty(stale.Evaluated);
    }

    [Fact]
    public void The_call_is_guarded_so_a_client_without_the_pill_cannot_throw()
    {
        // The bridge evaluates JS in a page whose runtime it does not control the build of; a missing
        // indicator must be a no-op, not an error dialog.
        var webView = new FakeNativeWebView();
        NativeHotReloadBridge.Attach(webView);

        RaskHotReload.RaiseApplied();

        Assert.All(webView.Evaluated, js => Assert.StartsWith("window.__raskHotReloadPill &&", js, StringComparison.Ordinal));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }
}
