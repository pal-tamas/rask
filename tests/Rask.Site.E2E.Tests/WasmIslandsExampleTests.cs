using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

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
        await Page.GotoAsync(Docs + "/islands");
        // Prerendered: the islands are on screen before the runtime exists, so a callback fired
        // now would reach nothing. Wait for the runtime to clear data-rask-prerendered (#1035).
        await WaitForInteractiveAsync();

        // Five runtimes on this page. Lit joined the other four in #938: it pairs with a plain .ts,
        // which is exactly how this app's own scoped TypeScript is spelled, and until the build learned
        // to tell the two apart by reading the C# base class a project could only have one of them.
        // Angular is still absent, for a reason of its own — three npm packages this bundle does not
        // carry.
        //
        // Counted rather than assumed, so adding a sixth here has to come with a decision about this
        // number — which is how Solid arrived: #958 added it to this page and this count stayed at
        // three, so the suite went red on main and named the omission.
        //
        // Still five with six islands on the page: the ColorPicker is a CHILD of the React counter. Its
        // component travels inside the counter's props and React renders both in one tree, so it has no
        // <rask-external> of its own — a sixth host here would mean children had stopped nesting.
        await Expect(Page.Locator("rask-external[data-rask-opaque]")).ToHaveCountAsync(5);

        // Mounted, not merely rendered: these nodes exist only because an adapter created them, which
        // means the chunk was fetched from the manifest and executed inside the browser-WASM host.
        await Expect(Page.GetByTestId("vue-chart")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("react-counter")).ToBeVisibleAsync();

        // The package island rendered inside its parent's React tree, not beside it.
        await Expect(Page.Locator("[data-testid=react-children] .react-colorful")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("svelte-meter")).ToBeVisibleAsync();

        // Solid earns a named assertion rather than only the count. It and React both compile .tsx, so
        // their Vite plugins are scoped by directory; get that wrong and the loser is built with the
        // other's JSX transform, which ships and mounts NOTHING. A bare count would still read 4 —
        // <rask-external> is Rask's element and exists whether or not the adapter ran — so what
        // distinguishes the two outcomes is a node only Solid's own transform can have produced.
        await Expect(Page.GetByTestId("solid-spark")).ToBeVisibleAsync();

        // Lit earns a named assertion for the same kind of reason. Its module is `LitBadge.ts`, and a
        // `.ts` beside a `.cs` is ALSO how a scoped asset is declared — so the failure this guards is
        // not a missing chunk but the file having gone down the other pipeline entirely: compiled into
        // obj/ as scoped JavaScript, absent from the bundle, with the host element rendered and empty.
        // Only a node the element's own `customElements.define` created can tell those apart.
        await Expect(Page.GetByTestId("lit-badge")).ToBeVisibleAsync();

        // The prop crossed as JSON and was assigned as a PROPERTY on the live element — the whole of
        // what the Lit adapter does.
        await Expect(Page.GetByTestId("lit-value")).ToHaveTextAsync("40");

        // Props crossed as JSON and arrived as data — one bar per C# record.
        await Expect(Page.Locator("[data-testid=vue-chart] button[data-label]")).ToHaveCountAsync(4);
        await Expect(Page.Locator("[data-testid=vue-chart] button[data-label=Jan]")).ToHaveCountAsync(1);

        // The same claim for Solid, whose prop is a plain IReadOnlyList<int>: six readings in C#, six
        // points in the sparkline.
        await Expect(Page.Locator("[data-testid=solid-spark] [data-testid^=solid-bar-]")).ToHaveCountAsync(6);
    });

    [Fact]
    public Task AVueCallbackReEntersCSharpThroughThisTabsRuntime() => RunAsync(async () =>
    {
        // The assertion this suite exists for. On the Server host the same click travels over the live
        // WebSocket; here there is no socket at all, and the handler id has to come back through
        // [JSExport] into the runtime running in this tab. Identical markup, entirely different path.
        await Page.GotoAsync(Docs + "/islands");
        // Prerendered: the islands are on screen before the runtime exists, so a callback fired
        // now would reach nothing. Wait for the runtime to clear data-rask-prerendered (#1035).
        await WaitForInteractiveAsync();
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
        await Page.GotoAsync(Docs + "/islands");
        // Prerendered: the islands are on screen before the runtime exists, so a callback fired
        // now would reach nothing. Wait for the runtime to clear data-rask-prerendered (#1035).
        await WaitForInteractiveAsync();
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

    /// <summary>
    ///     The Lit island's callback reaches C#, and its own state survives the re-render that follows.
    /// </summary>
    /// <remarks>
    ///     Both halves in one journey because the second is only interesting given the first: the nudge
    ///     re-enters C#, C# re-renders, and the element has to keep the count it owns while taking the
    ///     props it does not. The Lit adapter has no reconciler to lean on — it assigns properties onto
    ///     the live node — so "did not remount" is a claim about the adapter, not about a framework.
    /// </remarks>
    [Fact]
    public Task ALitCallbackReEntersCSharpAndTheElementKeepsItsOwnState() => RunAsync(async () =>
    {
        await Page.GotoAsync(Docs + "/islands");
        await WaitForInteractiveAsync();
        await Expect(Page.GetByTestId("lit-badge")).ToBeVisibleAsync();

        await Expect(Page.Locator("#island-badge-nudges")).ToHaveTextAsync("0");

        await Page.GetByTestId("lit-nudge").ClickAsync();
        await Expect(Page.Locator("#island-badge-nudges")).ToHaveTextAsync("1");

        // A prop change from C#. The element's own nudge count is not in the props and must survive it.
        await Page.Locator("#island-raise").ClickAsync();

        await Expect(Page.GetByTestId("lit-value")).ToHaveTextAsync("55");
        await Expect(Page.GetByTestId("lit-nudges")).ToHaveTextAsync("1");
    });

    /// <summary>
    ///     A React component straight from npm, nested inside a hand-written React island, calls back into C#, and a
    ///     later C# change reaches it as a prop update rather than a remount.
    /// </summary>
    /// <remarks>
    ///     The picker has no <c>.tsx</c>: its chain steps were generated from react-colorful's own declarations, and it
    ///     is a CHILD island, so its callback travels inside the parent's props and still has to reach C# through this
    ///     tab's runtime. The node probe is what "not a remount" means here — React reconciles the same component at the
    ///     same position, so the DOM node C# re-rendered around is the one that was there before.
    /// </remarks>
    [Fact]
    public Task APackageIslandNestedInAReactIslandCallsBackIntoCSharp() => RunAsync(async () =>
    {
        await Page.GotoAsync(Docs + "/islands");
        await WaitForInteractiveAsync();

        var picker = Page.Locator("[data-testid=react-children] .react-colorful");
        await Expect(picker).ToBeVisibleAsync();
        await Expect(Page.Locator("#island-color")).ToHaveTextAsync("#c026d3");

        // The hue slider is keyboard-operable: a key press moves it, react-colorful calls onChange with the new hex, and
        // that callback is the one C# wired.
        await picker.Locator(".react-colorful__hue .react-colorful__interactive").FocusAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(Page.Locator("#island-color")).Not.ToHaveTextAsync("#c026d3");

        // Mark the live node, then change the colour from C#.
        await picker.EvaluateAsync("node => { node.dataset.raskProbe = 'kept'; }");
        await Page.Locator("#island-reset").ClickAsync();

        await Expect(Page.Locator("#island-color")).ToHaveTextAsync("#c026d3");
        await Expect(picker).ToHaveAttributeAsync("data-rask-probe", "kept");
    });
}
