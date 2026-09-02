using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Four island runtimes in one Rask tree, driven in a real browser.
//
// Unit tests can reach the C# half of an island and the generated build inputs; they cannot reach the
// half that matters most here, which is what the ADAPTER does to the DOM. Everything asserted below
// is invisible to every other gate: that a chunk was fetched and mounted at all, that a callback
// crossed back into C#, and — the one that would otherwise rot silently — that a prop change is a
// reconcile rather than a remount.
[Collection(ServerExampleCollection.Name)]
public sealed class IslandsExampleTests(ServerExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Islands";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task EveryRuntimeMountsAndTakesItsCSharpProps() => RunAsync(async () =>
    {
        await Page.GotoAsync("/islands");

        // One host element per island, each a diff boundary Rask will never patch into.
        await Expect(Page.Locator("rask-external[data-rask-opaque]")).ToHaveCountAsync(4);

        // Mounted, not merely rendered: these nodes exist only because an adapter created them.
        await Expect(Page.GetByTestId("vue-chart")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("react-counter")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("svelte-meter")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("lit-badge")).ToBeVisibleAsync();

        // The props crossed as JSON and arrived as data: the Vue chart drew one bar per C# record.
        // Selected by data-label rather than by a class — the classes here are Tailwind utilities and
        // are free to change with the styling; the attribute is what the props actually produced.
        await Expect(Page.Locator("[data-testid=vue-chart] button[data-label]")).ToHaveCountAsync(4);
        await Expect(Page.Locator("[data-testid=vue-chart] button[data-label=Jan]")).ToHaveCountAsync(1);

        // Lit assigns props as PROPERTIES rather than attributes; an attribute would have stringified
        // the number and shown "[object Object]" for anything richer.
        await Expect(Page.GetByTestId("lit-revision")).ToHaveTextAsync("0");
    });

    [Fact]
    public Task AVueCallbackReEntersCSharpOverTheLiveSocket() => RunAsync(async () =>
    {
        await Page.GotoAsync("/islands");
        await Expect(Page.GetByTestId("vue-chart")).ToBeVisibleAsync();

        await Expect(Page.Locator("#island-last-clicked")).ToHaveTextAsync("(none)");

        // The click happens inside Vue's own subtree, on a node Rask never rendered. It reaches C#
        // through the same WebSocket every DOM handler uses — the island opens no channel of its own.
        await Page.Locator("[data-testid=vue-chart] button[data-label=Apr]").ClickAsync();

        await Expect(Page.Locator("#island-last-clicked")).ToHaveTextAsync("82");
        await Expect(Page.Locator("#island-clicks")).ToHaveTextAsync("1");
    });

    [Fact]
    public Task APropChangeReconcilesRatherThanRemounting() => RunAsync(async () =>
    {
        await Page.GotoAsync("/islands");
        await Expect(Page.GetByTestId("svelte-meter")).ToBeVisibleAsync();

        // Build up state that belongs to the front-end components and that C# has never seen.
        await Page.GetByTestId("meter-nudge").ClickAsync();
        await Page.GetByTestId("meter-nudge").ClickAsync();
        await Expect(Page.GetByTestId("meter-nudges")).ToHaveTextAsync("2");

        await Page.GetByTestId("react-add").ClickAsync();
        await Expect(Page.GetByTestId("react-total")).ToHaveTextAsync("1");

        await Expect(Page.GetByTestId("meter-value")).ToHaveTextAsync("40");

        // One C# re-render, changing a prop on all four islands at once.
        await Page.Locator("#island-raise").ClickAsync();

        // The new props arrived...
        await Expect(Page.GetByTestId("meter-value")).ToHaveTextAsync("55");
        await Expect(Page.GetByTestId("lit-revision")).ToHaveTextAsync("1");

        // ...and the components' OWN state survived it. This is the assertion the whole diff boundary
        // exists for: a remount would reset both counters to zero, and absolutely nothing else on the
        // page would look any different — which is why it needs pinning in a browser.
        await Expect(Page.GetByTestId("meter-nudges")).ToHaveTextAsync("2");
        await Expect(Page.GetByTestId("react-total")).ToHaveTextAsync("1");
    });

    [Fact]
    public Task TheBundleIsServedRatherThanFallingThroughToThePage() => RunAsync(async () =>
    {
        // The failure this pins (#884) is quiet and total: the island bundle lives under wwwroot but is
        // written after Content was globbed, so without an explicit static-web-asset registration
        // MapStaticAssets answers every chunk request with the page's own HTML. The client then reports
        // "Unexpected token '<'" and nothing mounts.
        var manifest = await Page.APIRequest.GetAsync(BaseUrl + "/_rask/external/manifest.json");
        Assert.True(manifest.Ok, $"the islands manifest was not served: HTTP {manifest.Status}");
        Assert.Contains("json", manifest.Headers["content-type"], StringComparison.OrdinalIgnoreCase);

        var table = (await manifest.JsonAsync())!.Value;
        foreach (var island in new[] { "VueChart", "ReactCounter", "SvelteMeter", "LitBadge" })
        {
            var chunk = table.GetProperty(island).GetString();
            Assert.False(string.IsNullOrEmpty(chunk), $"'{island}' has no chunk in the manifest");

            var response = await Page.APIRequest.GetAsync(BaseUrl + chunk);
            Assert.True(response.Ok, $"'{island}' chunk {chunk} was not served: HTTP {response.Status}");
            Assert.Contains("javascript", response.Headers["content-type"], StringComparison.OrdinalIgnoreCase);
        }
    });
}
