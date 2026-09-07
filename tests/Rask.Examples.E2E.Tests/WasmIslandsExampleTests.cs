using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Islands on the WASM host, in a real browser (#944).
//
// `docs/islands.md` said "both hosts, verified" while `IslandsExampleTests` bound to the Server
// collection only — nothing exercised the WASM islands page in a browser at all. The publish output was
// checked by hand and was correct, so this is not a known break; it is the absence of anything that
// would notice if it became one.
//
// What is host-specific here is the TRANSPORT, and it is the whole reason a second suite earns its
// keep. The front-end files are byte-identical copies of the Server showcase's, and nothing in them
// knows which host they are on — but a callback reaches C# through a [JSExport] call into this tab's
// own runtime rather than over a WebSocket. Every assertion below would pass on the Server host for
// reasons that say nothing about this one.
[Collection(WasmExampleCollection.Name)]
public sealed class WasmIslandsExampleTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "WasmIslands";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task EveryRuntimeMountsAndTakesItsCSharpProps() => RunAsync(async () =>
    {
        await Page.GotoAsync("/islands");

        // Three runtimes on this page, not the Server showcase's six: Lit, Solid and Angular are not
        // part of the WASM pair. Counted rather than assumed, so adding a fourth here has to come with
        // a decision about this number.
        await Expect(Page.Locator("rask-external[data-rask-opaque]")).ToHaveCountAsync(3);

        // Mounted, not merely rendered: these nodes exist only because an adapter created them, which
        // means the chunk was fetched from the manifest and executed inside the browser-WASM host.
        await Expect(Page.GetByTestId("vue-chart")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("react-counter")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("svelte-meter")).ToBeVisibleAsync();

        // Props crossed as JSON and arrived as data — one bar per C# record.
        await Expect(Page.Locator("[data-testid=vue-chart] button[data-label]")).ToHaveCountAsync(4);
        await Expect(Page.Locator("[data-testid=vue-chart] button[data-label=Jan]")).ToHaveCountAsync(1);
    });

    [Fact]
    public Task AVueCallbackReEntersCSharpThroughThisTabsRuntime() => RunAsync(async () =>
    {
        // The assertion this suite exists for. On the Server host the same click travels over the live
        // WebSocket; here there is no socket at all, and the handler id has to come back through
        // [JSExport] into the runtime running in this tab. Identical markup, entirely different path.
        await Page.GotoAsync("/islands");
        await Expect(Page.GetByTestId("vue-chart")).ToBeVisibleAsync();

        await Expect(Page.Locator("#island-last-clicked")).ToHaveTextAsync("(none)");

        // The click lands inside Vue's own subtree, on a node Rask never rendered and will never patch.
        await Page.Locator("[data-testid=vue-chart] button[data-label=Apr]").ClickAsync();

        await Expect(Page.Locator("#island-last-clicked")).ToHaveTextAsync("82");
        await Expect(Page.Locator("#island-clicks")).ToHaveTextAsync("1");
    });

    [Fact]
    public Task APropChangeReconcilesRatherThanRemounting() => RunAsync(async () =>
    {
        await Page.GotoAsync("/islands");
        await Expect(Page.GetByTestId("svelte-meter")).ToBeVisibleAsync();

        // State that belongs to the front-end component and that C# has never seen. If a prop change
        // remounted the island, this would reset — and the island would still look completely fine,
        // which is why it is asserted rather than eyeballed.
        await Page.GetByTestId("meter-nudge").ClickAsync();
        await Page.GetByTestId("meter-nudge").ClickAsync();
        await Expect(Page.GetByTestId("meter-nudges")).ToHaveTextAsync("2");

        // Now change a prop from C#.
        await Page.Locator("#island-raise").ClickAsync();

        // The Svelte-owned counter survived, so the adapter reconciled rather than tearing down.
        await Expect(Page.GetByTestId("meter-nudges")).ToHaveTextAsync("2");
    });
}
