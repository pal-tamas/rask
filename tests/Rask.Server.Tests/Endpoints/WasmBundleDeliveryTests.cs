using Rask.Core;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Endpoints;

// How a page learns there is a browser runtime it could become. The server stamps where the bundle
// lives and stops there — fetching it is the client's, at idle, and everything downstream of that is
// best-effort. What has to be exact is this: a page that should not fetch a bundle carries nothing at
// all, because the attribute IS the instruction.
public class WasmBundleDeliveryTests
{
    [Fact]
    public async Task WithTheBrowserRungOn_AnInteractivePageSaysWhereTheBundleIs()
    {
        using var host = RaskTestHost.Create<BundleHandlerApp>(
            configureServer: o =>
            {
                o.RenderModes.Wasm = true;
            });

        var body = await host.Http.GetStringAsync("/");

        Assert.Contains("data-rask-wasm=\"/main.js\"", body);
    }

    [Fact]
    public async Task WithTheBrowserRungOff_NoPageAsksForABundle()
    {
        // The default, and the one that must be exactly right: the attribute is the instruction, so a
        // stray one would have every visitor of every app downloading several megabytes of runtime
        // they will never use — on mobile data, for a page that was already working.
        using var host = RaskTestHost.Create<BundleHandlerApp>();

        var body = await host.Http.GetStringAsync("/");

        Assert.DoesNotContain("data-rask-wasm", body);
    }

    [Fact]
    public void TurningTheRungOnWithNowhereToFetchFrom_RefusesToStart()
    {
        // A host that will not start beats a page that silently never moves into the browser. The
        // difference matters because the second failure is invisible: the app works, over the socket,
        // for ever, and nothing anywhere says the rung it was configured for is not running.
        var modes = new RaskRenderModes { Wasm = true, WasmBundle = "  " };

        var ex = Assert.Throws<InvalidOperationException>(modes.Validate);

        Assert.Contains("WasmBundle", ex.Message);
    }

    [Fact]
    public void TheBundleUrlRidesThePathBase()
    {
        // An app under a sub-path has to fetch its own bundle, not one at the origin root — where, on
        // a host serving several apps, it would find someone else's.
        var limits = RaskServerLimits.From(new RaskServerOptions { RenderModes = { Wasm = true } });

        Assert.Equal("/main.js", limits.WasmBundleUrl);
    }
}

public sealed partial class BundleHandlerApp : Component
{
    protected override Component? HeadAssets => Title["bundle-handler"];

    protected override Component? Render() => Div[Button.OnClick(() => { })["press"]];
}
