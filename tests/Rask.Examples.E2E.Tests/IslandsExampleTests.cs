using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Six island runtimes in one Rask tree, driven in a real browser.
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
        await Expect(Page.Locator("rask-external[data-rask-opaque]")).ToHaveCountAsync(6);

        // Mounted, not merely rendered: these nodes exist only because an adapter created them.
        await Expect(Page.GetByTestId("vue-chart")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("react-counter")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("svelte-meter")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("lit-badge")).ToBeVisibleAsync();

        // Solid and React both compile .tsx, so these two mounting TOGETHER is what proves the
        // directory-scoped plugins routed each file to its own transform. Mis-scoped, one of them
        // builds green and mounts nothing at all.
        await Expect(Page.GetByTestId("solid-spark")).ToBeVisibleAsync();

        // Angular's bootstrap is the only asynchronous one, so this is also the assertion that the
        // promise resolved and the component was attached rather than left pending.
        await Expect(Page.GetByTestId("angular-ticker")).ToBeVisibleAsync();

        // Props crossed into Solid as data: one bar per C# reading.
        await Expect(Page.Locator("[data-testid=solid-spark] button.bar")).ToHaveCountAsync(6);

        // And into Angular through setInput, which is the only route that marks the view dirty — a
        // plain field assignment would update the instance and never repaint.
        await Expect(Page.GetByTestId("angular-quote")).ToHaveTextAsync("128");

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

        // Solid's own signal, and Angular's own field. Neither has ever been near C#.
        await Page.Locator("[data-testid=solid-spark] button.bar").First.HoverAsync();
        await Expect(Page.GetByTestId("solid-hovers")).ToHaveTextAsync("1");

        await Page.GetByTestId("angular-refresh").ClickAsync();
        await Expect(Page.GetByTestId("angular-ticks")).ToHaveTextAsync("1");

        await Expect(Page.GetByTestId("meter-value")).ToHaveTextAsync("40");

        // One C# re-render, changing a prop on all six islands at once.
        await Page.Locator("#island-raise").ClickAsync();

        // The new props arrived...
        await Expect(Page.GetByTestId("meter-value")).ToHaveTextAsync("55");
        await Expect(Page.GetByTestId("lit-revision")).ToHaveTextAsync("1");

        // ...and the components' OWN state survived it. This is the assertion the whole diff boundary
        // exists for: a remount would reset every counter to zero, and absolutely nothing else on the
        // page would look any different — which is why it needs pinning in a browser.
        //
        // Solid and Angular are the two worth having here on their own account. Solid's store is
        // reconciled rather than replaced, and Angular's props go through setInput on a component that
        // is never re-created; both are the kind of thing that keeps working right up until someone
        // simplifies the adapter into a remount.
        await Expect(Page.GetByTestId("meter-nudges")).ToHaveTextAsync("2");
        await Expect(Page.GetByTestId("react-total")).ToHaveTextAsync("1");
        await Expect(Page.GetByTestId("solid-hovers")).ToHaveTextAsync("1");
        await Expect(Page.GetByTestId("angular-ticks")).ToHaveTextAsync("1");
    });

    [Fact]
    public Task ASolidCallbackAndAnAngularOneBothReEnterCSharp() => RunAsync(async () =>
    {
        await Page.GotoAsync("/islands");
        await Expect(Page.GetByTestId("solid-spark")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("angular-ticker")).ToBeVisibleAsync();

        await Expect(Page.Locator("#island-hovered")).ToHaveTextAsync("(none)");

        // Solid's onMouseEnter fires inside a subtree Rask never rendered, and reaches C# over the
        // same socket every DOM handler uses.
        await Page.Locator("[data-testid=solid-spark] [data-testid=solid-bar-2]").HoverAsync();
        await Expect(Page.Locator("#island-hovered")).ToHaveTextAsync("2");

        // Angular's callback arrives as an @Input() the adapter set, so this also pins that a DELEGATE
        // prop survived setInput rather than being dropped as an unknown input.
        await Expect(Page.Locator("#island-quote")).ToHaveTextAsync("128");
        await Page.GetByTestId("angular-refresh").ClickAsync();
        await Expect(Page.Locator("#island-quote")).ToHaveTextAsync("135");
    });

    [Fact]
    public Task EverySourceTabOnThePageActuallyOpens() => RunAsync(async () =>
    {
        // CodeSample reads only the ACTIVE tab's source, so a file listed in Files() but never embedded
        // is an exception nothing notices until somebody clicks it — the first tab renders and the page
        // looks perfect. That is exactly how a missing `.ts` in the embed glob shipped here: every other
        // assertion in this class passed over a page whose Lit tab threw.
        await Page.GotoAsync("/islands");

        var tabs = Page.Locator(".sample-tab");
        var count = await tabs.CountAsync();
        Assert.True(count >= 7, $"expected the islands sample to list its sources, saw {count} tabs");

        for (var i = 0; i < count; i++)
        {
            await tabs.Nth(i).ClickAsync();

            // A tab whose source cannot be read throws inside the render, so the live update never
            // lands and the clicked tab never becomes the active one. Asserting that is what makes this
            // catch the unembedded file rather than merely exercising the click.
            await Expect(tabs.Nth(i)).ToHaveClassAsync(new Regex("active"));
        }
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
        foreach (var island in new[]
                 {
                     "VueChart", "ReactCounter", "SvelteMeter", "LitBadge", "SolidSpark", "AngularTicker",
                 })
        {
            var chunk = table.GetProperty(island).GetString();
            Assert.False(string.IsNullOrEmpty(chunk), $"'{island}' has no chunk in the manifest");

            var response = await Page.APIRequest.GetAsync(BaseUrl + chunk);
            Assert.True(response.Ok, $"'{island}' chunk {chunk} was not served: HTTP {response.Status}");
            Assert.Contains("javascript", response.Headers["content-type"], StringComparison.OrdinalIgnoreCase);
        }
    });
}
