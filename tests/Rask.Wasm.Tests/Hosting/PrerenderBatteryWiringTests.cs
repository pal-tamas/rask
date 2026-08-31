using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Wasm;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated chain entries

namespace Rask.Wasm.Tests.Hosting;

/// <summary>
///     The seam between the prerender pass and the <c>Rask</c> package's browser batteries.
/// </summary>
/// <remarks>
///     <para>
///         Prerendering returns from <c>RunAsync</c> before <c>BootAsync</c>, and the batteries are
///         applied inside <c>BootAsync</c>. Those two facts live in different pull requests and in
///         different assemblies, so nothing textual connects them — merged naively, the prerender pass
///         builds its container without the batteries, every page that injects one throws into the root
///         boundary, each route reports "threw — not written", and the publish exits 0 having written
///         nothing at all.
///     </para>
///     <para>
///         This asserts the composition rather than either part: a page that <b>needs</b> a
///         battery-registered service is prerendered successfully. Reverting the
///         <c>RaskWasmBatteryRegistry.Apply</c> call in the prerender branch fails it.
///     </para>
/// </remarks>
[Collection("RaskPrerenderEnvironment")]
public class PrerenderBatteryWiringTests
{
    [Fact]
    public async Task ThePrerenderPassSeesTheBatteriesTheBootPathWouldHaveApplied()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-" + Guid.NewGuid().ToString("N")[..8]);

        RouteRegistry.Replace(RouteGroup, [
            new RouteRegistration(typeof(NeedsABattery), "/needs-a-battery", null),
        ]);

        // Stands in for what the `Rask` package registers from its [ModuleInitializer]. Process-wide
        // state, which is why this class is serialised against the one asserting the variable is unset.
        RaskWasmBatteryRegistry.Use((_, services) => services.AddSingleton(new BatteryService()));
        Environment.SetEnvironmentVariable(WasmPrerender.OutputVariable, dir);

        try
        {
            await WasmHostBuilder.CreateDefault().RunAsync<NeedsABattery>();

            // Written, not merely attempted. A page that could not resolve its dependency renders the
            // boundary's error document instead, which WasmPrerender deliberately does not write — so
            // the file's existence is the only thing that distinguishes "the batteries were there" from
            // "the pass ran and quietly skipped everything".
            Assert.True(
                File.Exists(Path.Combine(dir, "needs-a-battery", "index.html")),
                "the page was not written — the prerender container did not get the batteries");
        }
        finally
        {
            Environment.SetEnvironmentVariable(WasmPrerender.OutputVariable, null);
            RaskWasmBatteryRegistry.Reset();
            // IOException covers DirectoryNotFoundException, which is the case where nothing was written.
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    // Stable RouteRegistry group name, independent of the test method's name.
    private const string RouteGroup = "PrerenderBatteryWiring";

    private sealed class BatteryService
    {
        public string Marker => "battery-ok";
    }

    private sealed class NeedsABattery(BatteryService battery) : Component
    {
        // Injected, not optional: without the batteries this constructor cannot be satisfied, the
        // render faults into the root boundary, and the page is deliberately not written — which is
        // exactly the failure being pinned.
        protected override Component? Render() => Div[battery.Marker];
    }
}
