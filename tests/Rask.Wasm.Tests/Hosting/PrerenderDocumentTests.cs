using Rask.Core;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated chain entries

namespace Rask.Wasm.Tests.Hosting;

/// <summary>
///     The WASM host writes the document around the App — the charset, the UI kit's stylesheet and its
///     theme scope — into the page it bakes, so an App is a title and a router here as on the server.
/// </summary>
/// <remarks>
///     Driven through <c>RunAsync</c>'s prerender branch rather than the boot path: it is the one that
///     writes a file this test can read, and it is the path a crawler sees, where a missing theme scope
///     would publish a grey page with a green build.
/// </remarks>
[Collection("RaskPrerenderEnvironment")]
public class PrerenderDocumentTests
{
    private const string RouteGroup = "PrerenderDocument";

    [Fact]
    public async Task A_baked_page_draws_with_the_kit_with_nothing_in_its_App()
    {
        var host = WasmHostBuilder.CreateDefault();

        var html = await BakeAsync(host);

        Assert.Contains(UiStylesheet.ThemeScopeAttribute, html, StringComparison.Ordinal);
        Assert.Contains(UiStylesheet.Href(), html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turning_the_kit_off_bakes_a_page_without_it()
    {
        var host = WasmHostBuilder.CreateDefault().Configure(c => c.Ui.Off());

        var html = await BakeAsync(host);

        Assert.DoesNotContain(UiStylesheet.ThemeScopeAttribute, html, StringComparison.Ordinal);
        Assert.DoesNotContain(UiStylesheet.Href(), html, StringComparison.Ordinal);
        Assert.Contains("charset=\"utf-8\"", html, StringComparison.Ordinal);
    }

    private static async Task<string> BakeAsync(WasmHostBuilder host)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rask-prerender-doc-" + Guid.NewGuid().ToString("N")[..8]);
        RouteRegistry.Replace(RouteGroup, [new RouteRegistration(typeof(PlainPage), "/plain", null)]);
        Environment.SetEnvironmentVariable(WasmPrerender.OutputVariable, dir);

        try
        {
            await host.RunAsync<PlainPage>();
            return File.ReadAllText(Path.Combine(dir, "plain", "index.html"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(WasmPrerender.OutputVariable, null);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    private sealed class PlainPage : Component
    {
        protected override Component? Render() => Div["plain"];
    }
}
