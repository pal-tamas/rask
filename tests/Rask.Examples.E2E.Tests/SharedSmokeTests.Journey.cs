using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// One comprehensive journey per hosting project (the user's directive: "1 e2e test per hosting
// project that walks through every page and tests every feature, plus every unusual user activity").
//
// The fine-grained framework/component LOGIC the old per-feature facts asserted (every validation
// attribute message, every nullable binding case, lifecycle hook ordering, diff codec, route-value
// parsing, …) is covered in-process by the unit suites (Rask.Core.Tests/Forms, Rask.Server.Tests,
// Rask.Validation.*.Tests, Rask.Example.Shared.Tests). What only a browser can prove — real DOM
// rendering, the live morph, scoped-CSS computed styles, JS interop, focus, drag events, history,
// reconnect, slow links — lives here, exercised once end-to-end against each host.
public abstract partial class SharedSmokeTests
{
    // Per-host gating. The showcase app is identical across hosts; only the *transport* and the
    // host's routing capabilities differ, so a flag toggles the steps a given host can run.
    protected sealed class ShowcaseJourneyOptions
    {
        // Host installs a SPA fallback, so deep links / refresh on a non-root route resolve
        // (Server, Wasm.Host). StandaloneWasm (WasmAppHost) 404s those — it walks via the sidebar.
        public bool DeepLink { get; init; }

        // Server holds session state over a WebSocket; dropping and restoring the socket must
        // preserve it. WASM runs in-process, so there is no socket to drop.
        public bool OfflineReconnect { get; init; }

        // Emulate a slow link (Chromium CDP) and confirm loading/placeholder states still settle.
        public bool Slow3g { get; init; }

        // No-SPA-fallback hosts (StandaloneWasm) can't refresh a deep route, but reloading the
        // /index.html shell must still boot the runtime cleanly.
        public bool ReloadShellBoots { get; init; }
    }

    protected const int HighlightSettleTimeoutMs = 35_000;

    // The heart of every host's single [Fact]. Sequential on purpose: it is a user session, not a
    // set — earlier steps establish the SPA context later ones rely on.
    protected async Task RunShowcaseJourneyAsync(ShowcaseJourneyOptions opts)
    {
        // Boot the shell once. NavigateToAsync("/") is a real GET on the SPA-fallback hosts and the
        // /index.html shell load on StandaloneWasm; from here the walk stays in-SPA via the sidebar.
        await NavigateToAsync("/");
        await Expect(Page.Locator("h1.display-5"))
            .ToContainTextAsync("The Rask framework",
                new LocatorAssertionsToContainTextOptions { Timeout = 60_000 });

        // Plant a sentinel on window — every in-SPA nav below must preserve it (proves no full
        // reload happened and the SPA context survived).
        await Page.EvaluateAsync("() => { window.__raskSentinel = 'alive'; }");

        await WalkDslAndComponentPagesAsync();
        await WalkInteractiveComponentPagesAsync();
        await WalkAuthAndContextPagesAsync();
        await WalkFormsPagesAsync();
        await WalkStylingDataAndAppPagesAsync();

        await TestInSessionNotFoundAsync();

        // The SPA sentinel must have survived the entire in-SPA walk.
        Assert.Equal("alive", await Page.EvaluateAsync<string?>("() => window.__raskSentinel"));

        await RunUnusualActivityAsync(opts);
    }

    // ---- helpers -------------------------------------------------------------------------------

    // In-SPA navigation via the sidebar + heading assertion. Works on every host once the shell is
    // loaded; on StandaloneWasm the sidebar click is the only navigation path available.
    private async Task SideAsync(string label, string heading, string headingSelector = "main h1.h2")
    {
        await ClickSidebar(label);
        await Expect(Page.Locator(headingSelector).First).ToContainTextAsync(heading,
            new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        // Global error-handling guard: no navigation in the walk may trip the framework's
        // top-level RootErrorBoundary ("Something went wrong"). A page that throws during render
        // would surface it here instead of its heading.
        await AssertNoGlobalCrashAsync();
    }

    // The framework's root error boundary renders a "Something went wrong" shell when an error
    // escapes every user boundary. Outside the deliberate /boom demos it must never appear.
    private async Task AssertNoGlobalCrashAsync() =>
        Assert.Equal(0, await Page.Locator(
            ".rask-error-boundary:has-text(\"Something went wrong\"), main:has-text(\"Something went wrong\")")
            .CountAsync());

    // ---- page walk -----------------------------------------------------------------------------

    private async Task WalkDslAndComponentPagesAsync()
    {
        // DSL group: Tag factories (blockquote), Primitives (Raw verbatim HTML), Universal props
        // (data-* expansion incl. bare null attribute).
        await SideAsync("Tag factories", "Tag factories");
        await Expect(Page.Locator(".sample-result-body blockquote").First)
            .ToContainTextAsync("A small DSL", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await SideAsync("Primitives", "Primitives");
        await Expect(Page.Locator(".sample-result-body p")
                .Filter(new LocatorFilterOptions { HasText = "Already" }).First.Locator("strong"))
            .ToHaveTextAsync("safe", new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });

        await SideAsync("Universal props", "Universal props");
        var dataDiv = Page.Locator(".sample-result-body div[data-role='card']").First;
        await Expect(dataDiv).ToHaveAttributeAsync("data-index", "7");
        await Expect(dataDiv).ToHaveAttributeAsync("data-new", ""); // bare null attribute
        // Accessibility props: Role/TabIndex render as native attrs, Aria expands to aria-* (the
        // dictionary keys verbatim) and a null value renders as a bare attribute on the icon.
        var ariaBtn = Page.Locator(".sample-result-body button[role='switch']").First;
        await Expect(ariaBtn).ToHaveAttributeAsync("aria-label", "Toggle dark mode");
        await Expect(ariaBtn).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(ariaBtn).ToHaveAttributeAsync("tabindex", "0");
        await Expect(ariaBtn.Locator("i")).ToHaveAttributeAsync("aria-hidden", "true");

        // HTML elements showcase: each of the 8 category pages renders its live demo of every element
        // in that group without tripping the root error boundary (SideAsync asserts the heading + no
        // crash). Spot-check a couple of rendered elements to confirm the demos actually produced DOM.
        await SideAsync("Text & inline", "Text & inline elements");
        await Expect(Page.Locator(".sample-result-body ruby").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await SideAsync("Grouping & lists", "Grouping & list elements");
        await Expect(Page.Locator(".sample-result-body ol[start='2'][reversed]").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await SideAsync("Sections & headings", "Sections & heading elements");
        await SideAsync("Form elements", "Form elements");
        await Expect(Page.Locator(".sample-result-body meter").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await SideAsync("Table elements", "Table elements");
        await SideAsync("Media & embedded", "Media & embedded elements");
        await SideAsync("Interactive", "Interactive elements");
        await Expect(Page.Locator(".sample-result-body details[open] summary").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await SideAsync("Document & metadata", "Document & metadata elements");

        // User components: generated factory greeting + [SkipFactory] counter that keeps its state.
        await SideAsync("User components", "User components");
        var greeting = Page.Locator(".sample-result-body p")
            .Filter(new LocatorFilterOptions { HasText = "Hello," }).First;
        await Expect(greeting).ToContainTextAsync("Dr.", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(greeting.Locator("strong")).ToHaveTextAsync("Ada");
        var skip = Page.Locator("#skipfactory-counter");
        await Expect(skip).ToContainTextAsync("Clicks: 7",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await skip.ClickAsync();
        await skip.ClickAsync();
        await skip.ClickAsync();
        await Expect(skip).ToContainTextAsync("Clicks: 10",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Routing: an in-handler Navigator.NavigateTo("/users/137") resolves through parent-route +
        // outlet, same path as a sidebar click.
        await SideAsync("Routing", "Routing");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "/users/137" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/users/137$"),
            new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("User #137",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });

        // Route + query params: the sidebar entry binds /users/42; the page shows the bound Id.
        await SideAsync("Route + query params", "User #42");
        await Expect(Page.Locator("li:has-text('Id')").Locator("strong")).ToHaveTextAsync("42");
        await Expect(Page).ToHaveTitleAsync("User #42 — Rask",
            new PageAssertionsToHaveTitleOptions { Timeout = 5_000 });

        // Adjacent text nodes: the toggle renders a literal ("Toggle ?tab=") directly beside a dynamic
        // value, which the browser coalesces into one text node. A diff-codec regression left the
        // dynamic half stale after SetQuery (the bug that surfaced here). Assert the label actually
        // flips on each click — exercises the coalesced-text UpdateText path on both transports.
        var tabToggle = Page.Locator("button.btn-primary:has-text('Toggle ?tab=')");
        await Expect(tabToggle).ToContainTextAsync("Toggle ?tab=profile",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await tabToggle.ClickAsync();
        await Expect(tabToggle).ToContainTextAsync("Toggle ?tab=activity",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await tabToggle.ClickAsync();
        await Expect(tabToggle).ToContainTextAsync("Toggle ?tab=profile",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Navigator: SetQuery mutates the URL and the in-SPA head-diff updates <title> across a
        // route-param change.
        await SideAsync("Navigator", "Navigator");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "SetQuery sort=asc" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*\\?sort=asc.*"),
            new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "ClearQuery" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/navigator$"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
    }

    private async Task WalkInteractiveComponentPagesAsync()
    {
        // Lifecycle: the awaited OnMountAsync continuation must run, and "Trigger re-render" bumps
        // the render counter.
        await SideAsync("Lifecycle", "Lifecycle hooks");
        await Expect(Page.Locator("li code:has-text('OnMountAsync (after')"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        var badge = Page.Locator(".badge:has-text('Render #')").First;
        await Page.WaitForTimeoutAsync(500);
        var before = ExtractRenderCount(await badge.TextContentAsync());
        await Page.Locator("button:has-text('Trigger re-render')").ClickAsync();
        await Expect(badge).Not.ToContainTextAsync($"Render #{before}",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Element refs: ElementRef → data-rask-ref → JS interop (focus a built-in, measure via user
        // scoped JS).
        await SideAsync("Element refs", "Element refs");
        var refInput = Page.Locator("main .sample-result-body input");
        await Page.Locator("main .sample-result-body button:has-text('Focus the input')").ClickAsync();
        await Expect(refInput).ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 10_000 });
        await Page.Locator("main .sample-result-body button:has-text('Measure the box')").ClickAsync();
        await Expect(Page.Locator("main .sample-result-body p"))
            .ToContainTextAsync("Box width:", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        // Scoped JS is loaded: the measure above invoked window.Rask["ElementRefDemo"].width, so the
        // per-component JS namespace must be present on window.
        Assert.True(
            await Page.EvaluateAsync<bool>("() => typeof window.Rask === 'object' && window.Rask !== null"),
            "scoped JS namespace window.Rask is missing — component JS did not load");

        // Code sample tabs + copy: the Element refs sample shows the real component
        // (ElementRefDemo.cs tab) and its sibling scoped JS (ElementRefDemo.js tab). Switching
        // tabs is a Rask state round-trip that swaps the highlighted pane; clicking copy runs the
        // scoped Rask.CodeSample.copy, which flashes "Copied!" (resilient to headless clipboard
        // restrictions via its execCommand fallback).
        var codeCard = Page.Locator("main .sample-code-col").First;
        await codeCard.Locator(".sample-tab:has-text('ElementRefDemo.js')").ClickAsync();
        await Expect(codeCard.Locator(".sample-code"))
            .ToContainTextAsync("getBoundingClientRect", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        // Regression: switching tabs swaps the highlighted pane by replacing one Raw value
        // (highlighted C#) with another (highlighted JS) over the live diff. The new markup must
        // be reparsed into REAL token <span> elements — not escaped into literal "<span …>" text
        // (which is what a textContent-based Raw update produced, ToContainText above can't catch
        // it because the escaped text still "contains" the substring). Require a real token span
        // element in the freshly-switched pane.
        await Expect(codeCard.Locator(".sample-code code span[class]").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await codeCard.Locator(".sample-copy").ClickAsync();
        await Expect(codeCard.Locator(".sample-copy"))
            .ToContainTextAsync("Copied!", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Live ticker: lifecycle hooks drive a zero-JS server-rendered SVG; switching symbol fires
        // OnPropsChanged without remounting.
        await SideAsync("Live ticker", "BTC live ticker");
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("BTC",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Expect(Page.Locator("#ticker-chart svg")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // Standalone proved a real bug here (publish-render rebuild dropped the history entry): the
        // sidebar nav above must have advanced the URL to /realtime/BTC.
        await Expect(Page).ToHaveURLAsync(new Regex(".*/realtime/BTC$"),
            new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
        await Page.Locator("#ticker-switch-ETH").ClickAsync();
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("ETH",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#ticker-log")).ToContainTextAsync("OnPropsChanged: Symbol BTC → ETH",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Background service: an app-wide singleton's loop pushes updates to two decoupled
        // subscribers. The tick badge must climb with NO user interaction — proof the
        // background producer (not a click handler) is driving the render.
        await SideAsync("Background service", "Background service");
        await Expect(Page.Locator("#metrics-chart svg")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        var firstTick = await ReadMetricsTickAsync();
        await Expect(Page.Locator("#metrics-tick")).Not.ToContainTextAsync($"tick {firstTick}",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        Assert.True(await ReadMetricsTickAsync() > firstTick, "the background feed did not advance on its own");

        // Cancellation: unmount a probe mid-delay → its CancellationToken fires and it logs cancelled.
        await SideAsync("Cancellation", "Cancellation");
        await Page.Locator("#cancel-mount").ClickAsync();
        await Expect(Page.Locator(".cancel-probe-pill")).ToContainTextAsync("running",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator("#cancel-unmount").ClickAsync();
        await Expect(Page.Locator(".cancel-log")).ToContainTextAsync("cancelled",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Disposal: sync IDisposable + async IAsyncDisposable both fire on unmount.
        await SideAsync("Disposal", "Disposal");
        await Page.Locator("#dispose-sync-mount").ClickAsync();
        await Expect(Page.Locator(".dispose-probe-pill")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Page.Locator("#dispose-sync-unmount").ClickAsync();
        await Expect(Page.Locator("#dispose-sync-log")).ToContainTextAsync("disposed",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator("#dispose-async-mount").ClickAsync();
        await Page.Locator("#dispose-async-unmount").ClickAsync();
        await Expect(Page.Locator("#dispose-async-log")).ToContainTextAsync("async-disposed",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Events: click counter, streaming input echo, form submit echo.
        await SideAsync("Events", "Events");
        var clickButton = Page.Locator(".sample-result-body button:has-text('Clicks:')").First;
        await clickButton.ClickAsync();
        await clickButton.ClickAsync();
        await Expect(clickButton).ToContainTextAsync("Clicks: 2",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator(".sample-result-body input[type=text]:not([name])").First.FillAsync("Hello Rask");
        await Expect(Page.Locator(".sample-result-body").Filter(new LocatorFilterOptions { HasText = "You typed:" }))
            .ToContainTextAsync("Hello Rask", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator("input[name=name]").FillAsync("Ada");
        await Page.Locator("button[type=submit]").ClickAsync();
        await Expect(Page.Locator(".sample-result-body").Filter(new LocatorFilterOptions { HasText = "Last submitted:" }))
            .ToContainTextAsync("Ada", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Full GlobalEventHandlers surface demo: OnDoubleClick (a MouseEventArgs event) and OnFocus
        // (a parameterless focus event) reach the C# handlers end-to-end and re-render the readouts —
        // proving the new universal event store dispatches over both transports, not just OnClick.
        var dblButton = Page.Locator(".sample-result-body button:has-text('Double-click')").First;
        await dblButton.DblClickAsync();
        await Expect(Page.Locator(".sample-result-body").Filter(new LocatorFilterOptions { HasText = "double-clicks:" }))
            .ToContainTextAsync("double-clicks: 1", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator(".sample-result-body div[tabindex='0']").First.ClickAsync();
        await Expect(Page.Locator(".sample-result-body").Filter(new LocatorFilterOptions { HasText = "last key:" }))
            .ToContainTextAsync("focused", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Virtualize: confirm it renders its source (the page now shows the demos via CodeSample)
        // and that the sticky header is pinned on the <th> cells — the fix for the old <thead>
        // sticky that flickered. Both are static checks (no scroll interaction) so this stays out
        // of the shared session's render-timing and can't destabilise later stateful steps.
        await SideAsync("Virtualize", "Virtualize");
        await Expect(Page.Locator("main .sample-code-col").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var thPosition = await Page.Locator("[data-testid=virtualize-scroller] thead th").First
            .EvaluateAsync<string>("el => getComputedStyle(el).position");
        Assert.Equal("sticky", thPosition);

        // Keyed lists: the page's contract is that a keyed reorder preserves the survivors'
        // DOM state — focus, caret, and uncommitted input text. Type into the first row,
        // place a caret mid-string, then reverse. We dispatch the reverse via a synthetic
        // (non-focus-stealing) click so the assertion isolates the reorder's effect on focus
        // from the focus the pointer would otherwise hand to the button itself. A keyed move
        // must relocate the live <li> node (Atomic Move), not detach + re-insert it.
        await SideAsync("Keyed lists", "Keyed lists");
        await Page.Locator("#kl-list li:nth-child(1) input.kl-note").ClickAsync();
        await Page.Locator("#kl-list li:nth-child(1) input.kl-note").FillAsync("travels");
        await Page.EvaluateAsync("() => document.activeElement.setSelectionRange(3, 3)");
        await Page.EvaluateAsync(
            "() => document.getElementById('kl-reverse').dispatchEvent(new MouseEvent('click', { bubbles: true }))");
        var keyedState = await Page.EvaluateAsync<string>(@"() => {
            const a = document.activeElement;
            if (!a || !a.classList || !a.classList.contains('kl-note')) return 'focus-lost';
            const li = a.closest('li');
            const name = li ? li.querySelector('span.fw-semibold').textContent.trim() : '?';
            return `${name}|${a.value}|${a.selectionStart}`;
        }");
        Assert.Equal("Apple|travels|3", keyedState);

        // Data table: every interaction is a URL query-param mutation → rebind → re-render.
        await SideAsync("Data table", "Data table");
        await Expect(Page.Locator("tbody tr")).ToHaveCountAsync(10,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Page.Locator("th button:has-text('Name')").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*[\\?&]sort=name"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        await Page.Locator("input[type='search']").FillAsync("Linus");
        await Page.WaitForTimeoutAsync(300);
        await Expect(Page).ToHaveURLAsync(new Regex(".*[\\?&]filter=Linus"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        var filtered = await Page.Locator("tbody tr").CountAsync();
        Assert.True(filtered is > 0 and < 10, $"filter should reduce rows; got {filtered}");
        await Page.Locator("input[type='search']").FillAsync("");
        await Page.Locator("select.form-select-sm").SelectOptionAsync("25");
        await Expect(Page.Locator("tbody tr")).ToHaveCountAsync(25,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        // Every showcase page shows its own source: the page-source CodeSample card is present.
        await Expect(Page.Locator("main .sample-card").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Master-detail: expand state is local; toggling inserts a keyed detail <tr> hosting a nested,
        // independently sortable plain <table>. Collapse removes it via the same keyed diff.
        await SideAsync("Master-detail", "Master-detail");
        await Expect(Page.Locator("#md-orders tbody tr.md-row")).ToHaveCountAsync(14,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Page.Locator("[data-testid='expander-1']").ClickAsync();
        await Expect(Page.Locator("[data-testid='inner-1']")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var innerRows = await Page.Locator("[data-testid='inner-1'] tbody tr").CountAsync();
        Assert.True(innerRows > 0, $"expanded order should reveal line items; got {innerRows}");
        // Sort the nested grid by Qty — the inner grid reacts on its own controlled state.
        await Page.Locator("[data-testid='inner-1'] th button:has-text('Qty')").First.ClickAsync();
        await Page.WaitForTimeoutAsync(200);
        var firstQtyAfter = await Page.Locator("[data-testid='inner-1'] tbody tr").First
            .Locator("td").Nth(2).InnerTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(firstQtyAfter), "inner grid should still render after sort");
        // Collapse: the keyed detail row is removed.
        await Page.Locator("[data-testid='expander-1']").ClickAsync();
        await Expect(Page.Locator("[data-testid='inner-1']")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        // Page-source CodeSample card is present.
        await Expect(Page.Locator("main .sample-card").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Drag & drop: native HTML5 drag events fire the C# handlers; the live diff morphs the DOM.
        await SideAsync("Drag & drop", "Headless drag & drop");
        await Expect(Page.Locator("#dd-fruit-list .dd-item")).ToHaveCountAsync(5,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await HtmlDragDropAsync("[data-testid='fruit-0']", "[data-testid='fruit-2']");
        await Expect(Page.Locator("#dd-fruit-list .dd-item").Nth(2)).ToContainTextAsync("Apple",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await HtmlDragDropAsync("[data-testid='card-2']", "[data-testid='card-5']");
        await Expect(Page.Locator("[data-testid='col-done'] [data-testid='card-2']")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Error boundary (the app's error handling): a handler-throw and a render-throw each trip
        // the nearest boundary's fallback — the error is contained, the navbar (outside the user
        // boundary) survives, and Recover restores the healthy subtree.
        await SideAsync("Error boundary", "Error boundary");
        await Page.Locator("#boom-throw").ClickAsync();
        await Expect(Page.Locator("#boom-fallback").First).ToContainTextAsync("kaboom — handler boundary demo",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(Page.Locator(".navbar .navbar-brand")).ToContainTextAsync("Rask"); // root boundary not tripped
        await Page.Locator("#boom-recover").First.ClickAsync();
        await Expect(Page.Locator("#boom-throw")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // Render-time throw: the HtmlSerializer rewinds the partial output and the boundary catches
        // it exactly once; Recover (which also clears the throw flag) restores the trigger.
        await Page.Locator("#boom-render-trigger").ClickAsync();
        await Expect(Page.Locator("#boom-fallback").First).ToContainTextAsync("kaboom — render-time boundary demo",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator("#boom-recover").First.ClickAsync();
        await Expect(Page.Locator("#boom-render-trigger")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    private async Task WalkAuthAndContextPagesAsync()
    {
        // Context: toggling a provider updates a deep consumer straight through a render-cached
        // intermediate (the diff-path change-detection bypass).
        await SideAsync("Context", "Context");
        var ctxBadge = Page.Locator("main .badge");
        await Expect(ctxBadge).ToContainTextAsync("Light", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator("button:has-text('Toggle theme')").ClickAsync();
        await Expect(ctxBadge).ToContainTextAsync("Dark", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Callback: a child's click invokes the parent's plain delegate and the framework auto-wraps
        // it to re-render the parent.
        await SideAsync("Callback", "Callback");
        var rating = Page.Locator("main .sample-result-body p");
        await Page.Locator("main .sample-result-body button").Nth(3).ClickAsync();
        await Expect(rating).ToContainTextAsync("You rated: 4/5",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Toast: Bootstrap toasts shown, stacked and dismissed entirely by live-diff state (no Bootstrap
        // JS, no data-bs-dismiss). Showing renders class="toast show"; the × removes it from the host list.
        await SideAsync("Toast", "Toast");
        var toast = Page.Locator("main .sample-result-body .toast.show");
        await Page.Locator("main .sample-result-body button:has-text('Show toast')").ClickAsync();
        await Expect(toast).ToContainTextAsync("Hello, world!",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await toast.Locator(".btn-close").ClickAsync();
        await Expect(toast).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        // User & auth: imperative gate (UserGate) + declarative Authorize slots, both re-rendering
        // live on IUserProvider.Changed with no reload.
        await SideAsync("User & auth", "User & auth gating");
        var gate = Page.Locator("#user-gate");
        await Expect(gate).ToContainTextAsync("signed out", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await gate.Locator("button:has-text('Sign in as user')").ClickAsync();
        await Expect(gate).ToContainTextAsync("Signed in as", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(gate).Not.ToContainTextAsync("Admin-only panel");
        await gate.Locator("button:has-text('Sign out')").ClickAsync();

        var demo = Page.Locator("#authorize-demo");
        await Expect(demo).ToContainTextAsync("Sign in to see member content",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await demo.Locator("button:has-text('Sign in as admin')").ClickAsync();
        await Expect(demo).ToContainTextAsync("Admin-only content",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        // The Authorized slot delegate re-runs with the fresh principal on Changed (no reload), so the
        // greeting names the admin who just signed in.
        await Expect(demo).ToContainTextAsync("welcome, rootadmin",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await demo.Locator("button:has-text('Sign out')").ClickAsync();
    }

    private async Task WalkFormsPagesAsync()
    {
        // Two-way binding: typed bind echo (the per-type / nullable / clear-to-null matrix is unit-
        // tested in Rask.Core.Tests/Forms — here we prove the live round trip for a text + a
        // change-only checkbox).
        await SideAsync("Two-way binding", "Two-way binding");
        await Page.Locator("input[name=Name]").First.FillAsync("Ada");
        await Expect(Page.Locator(".sample-result-body").Filter(new LocatorFilterOptions { HasText = "Hello," }))
            .ToContainTextAsync("Ada", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        // Scope to the live result pane — the embedded sample source (.sample-code) now shows the
        // demo's full class, which also contains "Subscribe =" in its echo template.
        var subscribeEcho = Page.Locator(".sample-result-body pre code").Filter(new LocatorFilterOptions { HasText = "Subscribe =" });
        var checkbox = Page.Locator("#bind-subscribe");
        await checkbox.ClickAsync();
        await Expect(subscribeEcho).ToContainTextAsync("Subscribe = true",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        // Highlighting is server-side: the code samples on this page must carry token spans after
        // the in-SPA morph (the morph must not flatten the Raw spans).
        await Expect(Page.Locator("pre code.language-csharp span").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Validation: an empty submit surfaces [Required]; a valid submit reaches the success banner;
        // the async validator shows "Checking…" then "taken". (Attribute-specific messages and the
        // latest-wins cancellation are unit-tested in Rask.Validation.DataAnnotations.Tests.)
        await SideAsync("Validation", "Validation");
        await Page.Locator("form:has(#v1-name) button[type=submit]").ClickAsync();
        await Expect(Page.Locator("form:has(#v1-name) .text-danger").First)
            .ToContainTextAsync("required",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000, IgnoreCase = true });
        var asyncForm = Page.Locator("form:has(#v3-username)");
        await asyncForm.Locator("#v3-username").FillAsync("admin");
        await asyncForm.Locator("#v3-username").BlurAsync();
        await Expect(asyncForm.Locator(".validating-indicator"))
            .ToContainTextAsync("Checking",
                new LocatorAssertionsToContainTextOptions { Timeout = 5_000, IgnoreCase = true });
        await Expect(asyncForm.Locator(".text-danger"))
            .ToContainTextAsync("taken",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000, IgnoreCase = true });

        // Floating labels: the reusable Floating* wrappers (input/select/textarea). An empty submit
        // surfaces the Bootstrap .invalid-feedback under a field (shown via .d-block, no is-invalid
        // toggle); a valid submit reaches the success banner. (Structure/id/label derivation is
        // unit-tested.)
        await SideAsync("Floating labels", "Floating labels");
        var floatingForm = Page.Locator("form:has(#ff-FullName)");
        await floatingForm.Locator("button[type=submit]").ClickAsync();
        await Expect(floatingForm.Locator(".invalid-feedback").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await floatingForm.Locator("#ff-FullName").FillAsync("Ada Lovelace");
        await floatingForm.Locator("#ff-Email").FillAsync("ada@example.com");
        await floatingForm.Locator("#ff-Age").FillAsync("30");
        await floatingForm.Locator("#ff-Plan").SelectOptionAsync("pro");
        await floatingForm.Locator("button[type=submit]").ClickAsync();
        await Expect(Page.Locator(".sample-result-body .alert-success")
                .Filter(new LocatorFilterOptions { HasText = "Ada Lovelace" }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Complex models (nested forms): render smoke (nested binding/validation is unit-tested).
        await SideAsync("Complex models", "Complex models");

        // Radio & checkbox groups: single-value radio bind + ICollection checkbox bind.
        await SideAsync("Radio & checkbox", "Radio & checkbox groups");
        var groups = Page.Locator("#groups-summary");
        await Page.Locator("input[type=radio][value='Pro']").CheckAsync();
        await Expect(groups).ToContainTextAsync("Plan: Pro", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator("input[type=checkbox][value='AI']").CheckAsync();
        await Expect(groups).ToContainTextAsync("AI", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Multi-select: the reusable MultiSelect<T> dropdown binds to an ICollection — open it (server
        // live-diff, no Bootstrap JS), pick an option and it appears as a live chip (the control re-renders
        // itself — no StateHasChanged). (Component mechanics are unit-tested in Demos/MultiSelectTests.)
        await SideAsync("Multi-select", "Multi-select");
        var multi = Page.Locator("#ms-interests");
        await multi.Locator(".form-select").ClickAsync(); // open the dropdown
        await multi.Locator(".dropdown-item").Filter(new LocatorFilterOptions { HasText = "AI" }).ClickAsync();
        await Expect(multi.Locator(".badge").Filter(new LocatorFilterOptions { HasText = "AI" }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // The menu stays open across selections; Escape closes it (the focusable box handles keydown — no JS).
        var openMenu = Page.Locator("#ms-interests .dropdown-menu.show");
        await Expect(openMenu).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await multi.Locator(".form-select").FocusAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(openMenu).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });

        // Re-open, then close by clicking outside — the transparent full-viewport backdrop catches it.
        await multi.Locator(".form-select").ClickAsync();
        await Expect(openMenu).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await multi.Locator(".position-fixed").ClickAsync();
        await Expect(openMenu).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });

        // Controlled MultiSelect (Value + OnChange, no Bind): selecting a topic flows out through OnChange
        // and the parent's summary updates — again with no StateHasChanged.
        var controlled = Page.Locator("#ms-controlled");
        await controlled.Locator(".form-select").ClickAsync();
        await controlled.Locator(".dropdown-item").Filter(new LocatorFilterOptions { HasText = "Tech" }).ClickAsync();
        await Expect(Page.Locator("#ms-controlled-summary")).ToContainTextAsync("Tech",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        // Close it again so the open dropdown's full-viewport backdrop doesn't intercept later navigation.
        await controlled.Locator(".position-fixed").ClickAsync();
        await Expect(Page.Locator("#ms-controlled .dropdown-menu.show")).ToBeHiddenAsync(
            new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });

        // Form controls page: every control in controlled (Value + OnChange) and bound (two-way) shape,
        // each with a derived readout rendered OUTSIDE the control / Form. Each readout must update live
        // with no StateHasChanged in the demo — including the Component-style controls (RadioGroup /
        // CheckboxGroup / MultiSelect) whose bound writes re-render the host via the binding owner.
        await SideAsync("Form controls", "Form controls");

        // Select — controlled + bound (native <select>; SelectOptionAsync matches by option value).
        await Page.Locator("#fc-select-controlled").SelectOptionAsync("Blazor");
        await Expect(Page.Locator("#fc-select-controlled-out")).ToContainTextAsync("Blazor",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator("#fc-select-bound").SelectOptionAsync("htmx");
        await Expect(Page.Locator("#fc-select-bound-out")).ToContainTextAsync("htmx",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Input — bound streams per keystroke into a readout outside the Form.
        await Page.Locator("#fc-input-bound").FillAsync("neo");
        await Expect(Page.Locator("#fc-input-bound-out")).ToContainTextAsync("neo",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // RadioGroup — bound (Component control): the derived readout sits OUTSIDE the Form yet updates.
        await Page.Locator("input[type=radio][name='fc-radio-b'][value='Team']").CheckAsync();
        await Expect(Page.Locator("#fc-radio-bound-out")).ToContainTextAsync("Team",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // CheckboxGroup — controlled.
        await Page.Locator("input[type=checkbox][name='fc-checkbox-c'][value='AI']").CheckAsync();
        await Expect(Page.Locator("#fc-checkbox-controlled-out")).ToContainTextAsync("AI",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // MultiSelect — bound: open, pick a topic, the readout outside the Form updates; then close so the
        // backdrop doesn't intercept later navigation.
        var fcMulti = Page.Locator("#fc-multiselect-bound");
        await fcMulti.Locator(".form-select").ClickAsync();
        await fcMulti.Locator(".dropdown-item").Filter(new LocatorFilterOptions { HasText = "Tech" }).ClickAsync();
        await Expect(Page.Locator("#fc-multiselect-bound-out")).ToContainTextAsync("Tech",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await fcMulti.Locator(".position-fixed").ClickAsync();
        await Expect(fcMulti.Locator(".dropdown-menu.show")).ToBeHiddenAsync(
            new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });
    }

    private async Task WalkStylingDataAndAppPagesAsync()
    {
        // SVG: render smoke.
        await SideAsync("SVG", "SVG", "main h1.h2");

        // Scoped CSS: two components get distinct scope ids → distinct computed background colors.
        await SideAsync("Scoped CSS", "Scoped CSS");
        var boxes = Page.Locator(".sample-result-body .box");
        await Expect(boxes).ToHaveCountAsync(2, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        var bg0 = await boxes.Nth(0).EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        var bg1 = await boxes.Nth(1).EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.NotEqual(bg0, bg1);
        // The scoped stylesheet actually loaded and applied: the rule painted a real colour rather
        // than leaving the default transparent background.
        Assert.NotEqual("rgba(0, 0, 0, 0)", bg0);

        // Global (non-scoped) styles live in wwwroot/global.css, linked from App's <Head> — not in a
        // scoped {Component}.css (there is no :global() opt-out). On WASM the App's <Head> <link>s are
        // injected client-side after boot, so both the link and its computed effect may lag the first
        // read — poll for each rather than asserting once.
        await Expect(Page.Locator("head link[rel='stylesheet'][href$='/global.css']"))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        // The brand palette actually overrides Bootstrap's :root defaults (global.css loads after it).
        await Page.WaitForFunctionAsync(
            "() => getComputedStyle(document.documentElement).getPropertyValue('--bs-primary').trim() === '#7C3AED'",
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        // The navbar height is centralised in a single --nav-h custom property (no hard-coded 56px),
        // and on desktop the sidebar uses it to become an independent, viewport-bounded scroll region
        // so its long link list scrolls inside itself rather than stretching the page — the
        // "navbar too tall" fix. At the default 1280×720 viewport the 35-link list overflows the box.
        Assert.Equal("56px", (await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.documentElement).getPropertyValue('--nav-h').trim()")));
        var navScroll = await Page.Locator("aside.side-nav .position-sticky").First.EvaluateAsync<string>(
            @"el => {
                const cs = getComputedStyle(el);
                const navH = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--nav-h'));
                return JSON.stringify({
                    overflowY: cs.overflowY,
                    bounded: el.clientHeight <= window.innerHeight - navH + 1,
                    scrollable: el.scrollHeight > el.clientHeight,
                });
            }");
        Assert.Contains("\"overflowY\":\"auto\"", navScroll);
        Assert.Contains("\"bounded\":true", navScroll);
        Assert.Contains("\"scrollable\":true", navScroll);

        // Asset loading: per-component content-addressed <link>s, a JS-only <script>, and lazy
        // mount adding/removing a link via the keyed head-morph.
        await SideAsync("Asset loading", "Asset loading", "main h1.h3");
        var cssLinkSel = "head link[rel='stylesheet'][href^='/_rask/a/']";
        Assert.True(await Page.Locator(cssLinkSel).CountAsync() >= 3, "expected >=3 per-component CSS links");
        Assert.True(await Page.Locator("head script[src^='/_rask/a/'][src$='.js']").CountAsync() >= 1,
            "expected >=1 JS-only script");
        // Each section is now a CodeSample: source beside the live result. Assert all four cards mount
        // (the scoped-asset live results above are the CodeSample Result panes).
        Assert.True(await Page.Locator("main .sample-card").CountAsync() >= 4,
            "expected >=4 CodeSample cards on the asset-loading page");
        var beforeLazy = await Page.Locator(cssLinkSel).CountAsync();
        // Warm-up toggle: LazyChild is never instantiated until shown, so its scoped stylesheet
        // (.lazy-child → #fff4d6) is not among the page-load prefetches. Mount it once and unmount
        // it to load + cache LazyChild.css, so the *measured* mount below hits a warm cache. The
        // no-FOUC guard's contract is "no flash once the sheet is available" (it preloads the
        // <link> and holds the body paint until .sheet applies, bounded by a 500ms cap so a
        // pathologically slow sheet shows a brief flash rather than stalling navigation); warming
        // first asserts the guard deterministically instead of racing that cap on a slow runner.
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { NameString = "Show LazyChild" }).ClickAsync();
        await Expect(Page.Locator(".lazy-child")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        Assert.True(await Page.Locator(cssLinkSel).CountAsync() > beforeLazy, "lazy mount should add a CSS link");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { NameString = "Hide LazyChild" }).ClickAsync();
        await Expect(Page.Locator(".lazy-child")).ToHaveCountAsync(0);
        // The no-FOUC preload appends a clone of the new scoped <link>; the keyed head-morph must
        // keep that one element (not duplicate it) and remove it on unmount. Assert the per-
        // component link count returns to its pre-mount value — guards clone accumulation.
        await Expect(Page.Locator(cssLinkSel)).ToHaveCountAsync(beforeLazy,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        // Measured mount (warm cache): assert the fix's deterministic contract — the scoped
        // stylesheet's rule is APPLIED in the CSSOM at the instant .lazy-child is inserted into
        // the DOM. The runtime preloads the <link> and awaits its `.sheet` before the body morph,
        // so the rule is live before the styled node exists and the browser paints it styled.
        // (getComputedStyle is NOT used: a freshly recalc'd element can read its pre-application
        // value for a frame even when the sheet is applied and no flash actually paints — that
        // measurement artifact is unrelated to FOUC.) Without the fix the <link> would parse on a
        // later task, so the rule would be absent at insertion (sheetAppliedAtInsert === false).
        await Page.EvaluateAsync(@"() => {
            window.__raskLazyApplied = null;
            const obs = new MutationObserver(() => {
                if (window.__raskLazyApplied !== null) return;
                if (!document.querySelector('.lazy-child')) return;
                let applied = false;
                document.head.querySelectorAll('link[rel=""stylesheet""]').forEach((l) => {
                    if (!l.sheet) return;
                    try { for (const r of l.sheet.cssRules) if (r.cssText.indexOf('lazy-child') >= 0) applied = true; } catch (e) {}
                });
                window.__raskLazyApplied = applied;
                obs.disconnect();
            });
            obs.observe(document.documentElement, {
                childList: true, subtree: true, attributes: true, attributeFilter: ['class']
            });
        }");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { NameString = "Show LazyChild" }).ClickAsync();
        await Expect(Page.Locator(".lazy-child")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        Assert.True(await Page.Locator(cssLinkSel).CountAsync() > beforeLazy, "lazy mount should add a CSS link");
        await Page.WaitForFunctionAsync("() => window.__raskLazyApplied !== null",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        var lazySheetApplied = await Page.EvaluateAsync<bool>("() => window.__raskLazyApplied === true");
        Assert.True(lazySheetApplied,
            "scoped stylesheet must be applied before .lazy-child is inserted (no FOUC)");
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { NameString = "Hide LazyChild" }).ClickAsync();
        await Expect(Page.Locator(".lazy-child")).ToHaveCountAsync(0);
        // The no-FOUC preload appends a clone of the new scoped <link>; the keyed head-morph
        // must keep that one element (not duplicate it) and remove it on unmount. Assert the
        // per-component link count returns to its pre-mount value — guards clone accumulation.
        await Expect(Page.Locator(cssLinkSel)).ToHaveCountAsync(beforeLazy,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        // HttpClient + DI: an injected HttpClient loads a card in OnMountAsync.
        await SideAsync("HttpClient + DI", "HttpClient + DI");
        await Expect(Page.Locator(".sample-result-body article.card")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        // File upload / download: render smoke (sinks are unit-tested).
        await SideAsync("File upload", "File upload", "main h1.h2");
        await SideAsync("File download", "File download", "main h1.h2");

        // Todos: full CRUD + URL-driven dialog. Add, edit, toggle, delete.
        await SideAsync("Todos", "Todos");
        await Expect(Page.Locator(".list-group .list-group-item")).ToHaveCountAsync(2,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Page.Locator("button:has-text('New todo')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos/new$"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        // Dialog opens centered over a dim backdrop; clicking the backdrop cancels (back to /todos).
        await Expect(Page.Locator("dialog[open]")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        await Expect(Page.Locator(".todo-backdrop")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        // The dialog auto-focuses on open (OnRenderedAsync → ElementRef.FocusAsync), so the
        // keyboard primitive works with no prior click. This is deterministic on both hosts: the
        // focus helper retries on the next frame, so a focus issued during a render (before the
        // DOM patch on WASM) still lands once the <dialog> gains its `open` attribute.
        await Expect(Page.Locator("dialog[open]")).ToBeFocusedAsync(
            new LocatorAssertionsToBeFocusedOptions { Timeout = 5_000 });
        // Escape closes the focused dialog: OnKeyDown on the <dialog> routes Escape to cancel.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos$"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        // Reopen, then dismiss via a backdrop click.
        await Page.Locator("button:has-text('New todo')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos/new$"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        await Expect(Page.Locator("dialog[open]")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        // Click a corner — the backdrop's centre is covered by the centered dialog.
        await Page.Locator(".todo-backdrop").ClickAsync(
            new LocatorClickOptions { Position = new Position { X = 8, Y = 8 } });
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos$"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        // Reopen for the rest of the flow.
        await Page.Locator("button:has-text('New todo')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos/new$"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        // Empty submit → [Required].
        await Page.Locator("button:has-text('Add')").ClickAsync();
        await Expect(Page.Locator(".text-danger.small")).ToContainTextAsync("Title is required",
            new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
        await Page.Locator("#todo-title").FillAsync("Wire up reconnect");
        await Page.Locator("button:has-text('Add')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos$"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        await Expect(Page.Locator(".list-group .list-group-item")).ToHaveCountAsync(3,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        // Toggle the first item's checkbox → completed class.
        var firstTitle = await Page.Locator(".list-group-item .todo-title").First.InnerTextAsync();
        await Page.Locator(".list-group-item").First.Locator("input[type='checkbox']").CheckAsync();
        await Expect(Page.Locator(".todo-title.completed", new PageLocatorOptions { HasTextString = firstTitle }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        // Delete one row.
        var rowsBefore = await Page.Locator(".list-group .list-group-item").CountAsync();
        await Page.Locator(".list-group-item button:has(i.bi-trash)").First.ClickAsync();
        await Expect(Page.Locator(".list-group .list-group-item")).ToHaveCountAsync(rowsBefore - 1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        // Page-source CodeSample card is present.
        await Expect(Page.Locator("main .sample-card").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // IJSRuntime: sessionStorage set/read/remove round-trip through the unified IJSRuntime.
        // The interactive demo now lives in the CodeSample Result pane beside its own source.
        await ClearJsRuntimeStorageAsync();
        await SideAsync("IJSRuntime", "IJSRuntime", "main h1.h2");
        await Expect(Page.Locator("main .sample-card .sample-result-body #demo-input")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Page.Locator("#demo-input").FillAsync("hello-rask");
        await Page.Locator("#demo-set").ClickAsync();
        await Expect(Page.Locator("#demo-status")).ToContainTextAsync("Set to: hello-rask",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator("#demo-read").ClickAsync();
        await Expect(Page.Locator("#demo-last-read")).ToHaveTextAsync("hello-rask",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
        await Page.Locator("#demo-remove").ClickAsync();
        await Expect(Page.Locator("#demo-status")).ToContainTextAsync("Removed",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await TestBrowserApisAsync();
    }

    // Browser APIs section: one example page per typed wrapper, each over the unified IJSRuntime and
    // identical on Server and WASM. Clipboard + geolocation are granted on the context
    // (SharedSmokeTests.InitializeAsync), so those branches resolve rather than permission-faulting.
    private async Task TestBrowserApisAsync()
    {
        var contains = new LocatorAssertionsToContainTextOptions { Timeout = 10_000 };
        var visible = new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 };

        // Storage — localStorage round-trip via IBrowserStorage.
        await SideAsync("Storage", "Storage");
        await Page.Locator("#storage-input").FillAsync("persist-me");
        await Page.Locator("#storage-set").ClickAsync();
        await Expect(Page.Locator("#storage-status")).ToContainTextAsync("Stored: persist-me", contains);
        await Page.Locator("#storage-read").ClickAsync();
        await Expect(Page.Locator("#storage-read-value")).ToHaveTextAsync("persist-me",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });

        // Cookies — set then read back via ICookies (document.cookie).
        await SideAsync("Cookies", "Cookies");
        await Page.Locator("#cookie-input").FillAsync("choco");
        await Page.Locator("#cookie-set").ClickAsync();
        await Expect(Page.Locator("#cookie-status")).ToContainTextAsync("Set: choco", contains);
        await Page.Locator("#cookie-get").ClickAsync();
        await Expect(Page.Locator("#cookie-read-value")).ToHaveTextAsync("choco",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });

        // Clipboard — copy then read back the same text via IClipboard.
        await SideAsync("Clipboard", "Clipboard");
        await Page.Locator("#clipboard-copy").ClickAsync();
        await Expect(Page.Locator("#clipboard-status")).ToContainTextAsync("Copied to clipboard", contains);
        await Page.Locator("#clipboard-paste").ClickAsync();
        await Expect(Page.Locator("#clipboard-read-value")).ToContainTextAsync("Copied from Rask!", contains);

        // Geolocation — IGeolocation via the __raskApi.geolocation Promise helper (fixed context fix).
        await SideAsync("Geolocation", "Geolocation");
        await Page.Locator("#geo-get").ClickAsync();
        await Expect(Page.Locator("#geo-value")).ToContainTextAsync("lat 51.5", contains);

        // Permissions — geolocation was granted on the context, so the query reports Granted.
        await SideAsync("Permissions", "Permissions");
        await Page.Locator("#perm-geo").ClickAsync();
        await Expect(Page.Locator("#perm-geo-value")).ToContainTextAsync("Granted", contains);

        // Browser info — navigator property reads returned directly by the invoke dispatcher.
        await SideAsync("Browser info", "Browser info");
        await Page.Locator("#nav-read").ClickAsync();
        await Expect(Page.Locator("#nav-value")).ToContainTextAsync("online:", contains);

        // Page visibility — the test page is foreground, so the state is Visible.
        await SideAsync("Page visibility", "Page visibility");
        await Page.Locator("#vis-read").ClickAsync();
        await Expect(Page.Locator("#vis-value")).ToContainTextAsync("Visible", contains);

        // Vibration is device-dependent (often unsupported in headless desktop), so smoke-check that the
        // page renders its control rather than asserting an outcome.
        await SideAsync("Vibration", "Vibration");
        await Expect(Page.Locator("#vibrate-buzz")).ToBeVisibleAsync(visible);

        // Network info — Chromium exposes navigator.connection; assert the read populated the readout
        // (the exact class differs by browser, so just confirm it's no longer the idle placeholder).
        await SideAsync("Network info", "Network info");
        await Page.Locator("#net-read").ClickAsync();
        await Expect(Page.Locator("#net-value")).Not.ToContainTextAsync("not requested", contains);

        // Media queries — matchMedia is universally supported; the readout shows the evaluated booleans.
        await SideAsync("Media queries", "Media queries");
        await Page.Locator("#media-read").ClickAsync();
        await Expect(Page.Locator("#media-value")).ToContainTextAsync("prefersDark:", contains);

        // Speech — audio can't be asserted headlessly, so smoke-check the page renders its control.
        await SideAsync("Speech", "Speech");
        await Expect(Page.Locator("#speech-speak")).ToBeVisibleAsync(visible);

        // Screen info — window.screen is always available; assert the read populated the readout.
        await SideAsync("Screen info", "Screen info");
        await Page.Locator("#screen-read").ClickAsync();
        await Expect(Page.Locator("#screen-value")).ToContainTextAsync("DPR", contains);

        // Quota estimate — Chromium supports navigator.storage.estimate; assert the read populated it
        // (unsupported browsers say "not supported" — either way it leaves the idle placeholder). The nav
        // label avoids the word "Storage" so it doesn't collide with the existing "Storage" sidebar entry.
        await SideAsync("Quota estimate", "Quota estimate");
        await Page.Locator("#storage-est-read").ClickAsync();
        await Expect(Page.Locator("#storage-est-value")).Not.ToContainTextAsync("not requested", contains);

        // Visual viewport — window.visualViewport is available in headless Chromium; assert the read.
        await SideAsync("Visual viewport", "Visual viewport");
        await Page.Locator("#vv-read").ClickAsync();
        await Expect(Page.Locator("#vv-value")).ToContainTextAsync("scale", contains);

        // Broadcast channel — exercises the full JS→C# push round-trip (BroadcastChannel.onmessage →
        // static [JSInvokable] → handler → StateHasChanged) on every host, incl. trimmed WASM. The page
        // opens a sender + receiver on one name, so a post is delivered to the receiver in the same page.
        await SideAsync("Broadcast channel", "Broadcast channel");
        await Page.Locator("#bc-send").ClickAsync();
        await Expect(Page.Locator("#bc-log")).ToContainTextAsync("Message #1", contains);

        // Intersection observer — another JS→C# push: scroll the (initially below-the-fold) target into
        // view and the browser pushes the change → static [JSInvokable] → handler → StateHasChanged. Starts
        // "out of view"; becomes "in view" after the scroll. Validates the round-trip on every host.
        await SideAsync("Intersection observer", "Intersection observer");
        await Expect(Page.Locator("#io-status")).ToContainTextAsync("out of view", contains);
        await Page.Locator("#io-target").ScrollIntoViewIfNeededAsync();
        await Expect(Page.Locator("#io-status")).ToContainTextAsync("in view", contains);

        // Resize observer — another ElementRef + JS→C# push: the observer fires once on observe with the
        // box's current size, so the readout shows pixels (proves the round-trip) on every host.
        await SideAsync("Resize observer", "Resize observer");
        await Expect(Page.Locator("#resize-value")).ToContainTextAsync("px", contains);

        // Mutation observer — another ElementRef + JS→C# push: mutate the watched box's DOM and the
        // browser pushes each MutationRecord → static [JSInvokable] → handler. Adding a child bumps the
        // childList tally; toggling the box's class bumps the attribute tally. Validates both record types.
        await SideAsync("Mutation observer", "Mutation observer");
        await Expect(Page.Locator("#mo-child")).ToContainTextAsync("0", contains);
        await Page.Locator("#mo-add").ClickAsync();
        await Expect(Page.Locator("#mo-child")).ToContainTextAsync("1", contains);
        await Page.Locator("#mo-toggle").ClickAsync();
        await Expect(Page.Locator("#mo-attr")).ToContainTextAsync("1", contains);

        // Media session — publish now-playing metadata to navigator.mediaSession (chromium supports the
        // setter headless). Proves the one-shot IMediaSession round-trip; the media-key action handlers
        // can't be exercised without OS media keys, so they're covered by unit tests.
        await SideAsync("Media session", "Media session");
        await Page.Locator("#ms-publish").ClickAsync();
        await Expect(Page.Locator("#ms-status")).ToContainTextAsync("published", contains);

        // Gamepad — chromium exposes navigator.getGamepads, so IsSupported resolves true on both hosts and
        // the watch starts (status flips to "Ready"). No virtual pad is connected headless, so the count
        // stays 0; the per-pad readings are covered by unit tests.
        await SideAsync("Gamepad", "Gamepad");
        await Expect(Page.Locator("#gamepad-status")).ToContainTextAsync("Ready", contains);
        await Expect(Page.Locator("#gamepad-count")).ToContainTextAsync("0", contains);

        // Device sensors — chromium exposes DeviceOrientationEvent (no iOS prompt), so Start grants and
        // begins watching on both hosts; the status flips to "listening" even though no sensor data flows
        // headless. Proves the IsSupported/RequestPermission/WatchAsync round-trip; readings are unit-tested.
        await SideAsync("Device sensors", "Device sensors");
        await Page.Locator("#sensor-start").ClickAsync();
        await Expect(Page.Locator("#sensor-status")).ToContainTextAsync("listening", contains);

        // Live location — geolocation watch (push). The context grants permission + a fixed fix (51.5074),
        // so starting the watch pushes that position via watchPosition → static [JSInvokable] → handler.
        await SideAsync("Live location", "Live location");
        await Page.Locator("#geowatch-start").ClickAsync();
        await Expect(Page.Locator("#geowatch-value")).ToContainTextAsync("51.5", contains);

        // Web Crypto — crypto.subtle.digest of the default input "hello" is a known SHA-256 constant, so
        // the hash is deterministic across hosts (validates the round-trip + hex encoding).
        await SideAsync("Web Crypto", "Web Crypto");
        await Page.Locator("#crypto-hash").ClickAsync();
        await Expect(Page.Locator("#crypto-hash-value"))
            .ToContainTextAsync("2cf24dba5fb0a30e26e83b2ac5b9e29e", contains);

        // Performance — the page has long since loaded, so the navigation entry yields timing in ms.
        await SideAsync("Performance", "Performance");
        await Page.Locator("#perf-read").ClickAsync();
        await Expect(Page.Locator("#perf-value")).ToContainTextAsync("ms", contains);

        // IndexedDB — a real async set→get round-trip through the transaction-wrapped helper. Set the
        // default value, read it back, and assert it returns (validates IndexedDB on every host).
        await SideAsync("IndexedDB", "IndexedDB");
        await Page.Locator("#idb-set").ClickAsync();
        await Page.Locator("#idb-get").ClickAsync();
        await Expect(Page.Locator("#idb-read")).ToContainTextAsync("hello from IndexedDB", contains);
    }

    // In-session navigation to an unknown route (client pushState + popstate — the same signal
    // rask.js forwards to the live session). Works on every host (no deep-link needed): the
    // [NotFound] page renders inside the layout shell and no sidebar entry stays active.
    private async Task TestInSessionNotFoundAsync()
    {
        await Page.EvaluateAsync(@"() => {
            history.pushState({ rask: true }, '', '/in-session-missing');
            window.dispatchEvent(new PopStateEvent('popstate'));
        }");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Page not found",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator("aside.side-nav button.nav-item-btn-active")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        // "Back to welcome" is an in-session nav to "/" — returns us to a known page so the journey
        // can continue, and proves recovery from the not-found state.
        await Page.Locator("main button:has-text(\"Back to welcome\")").ClickAsync();
        await Expect(Page.Locator("h1.display-5")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    // ---- unusual user activity -----------------------------------------------------------------

    private async Task RunUnusualActivityAsync(ShowcaseJourneyOptions opts)
    {
        // Back / forward: history navigation must preserve the SPA sentinel and resolve both ends.
        await SideAsync("Tag factories", "Tag factories");
        await Page.GoBackAsync();
        Assert.Equal("alive", await Page.EvaluateAsync<string?>("() => window.__raskSentinel"));
        await Page.GoForwardAsync();
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Tag factories",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });

        if (opts.DeepLink)
        {
            // Refresh on a deep CodeSample route must re-render the page (not the RootErrorBoundary)
            // and re-emit server-highlighted spans.
            await Page.GotoAsync("/validation");
            await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
            await Page.ReloadAsync();
            Assert.Equal(0, await Page.Locator(".rask-error-boundary h1:has-text(\"Something went wrong\")").CountAsync());
            await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
            await WaitForHighlightedSpansAsync(HighlightSettleTimeoutMs);
            var total = await Page.Locator("pre code[class*='language-']").CountAsync();
            var highlighted = await Page.Locator("pre code[class*='language-']:has(span[class])").CountAsync();
            Assert.True(total > 0 && total == highlighted,
                $"/validation after refresh: {highlighted}/{total} highlighted.");

            // A deep link to an unknown route renders the [NotFound] page inside the layout shell.
            await Page.GotoAsync("/this-route-definitely-does-not-exist");
            await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Page not found",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        }

        if (opts.ReloadShellBoots)
        {
            // WasmAppHost serves only /index.html; a reload there must always boot the runtime.
            await Page.GotoAsync("/index.html");
            await Page.ReloadAsync();
            await Expect(Page.Locator("h1.display-5"))
                .ToContainTextAsync("The Rask framework",
                    new LocatorAssertionsToContainTextOptions { Timeout = 60_000 });
        }

        if (opts.Slow3g)
        {
            // Emulate a slow link via Chromium CDP and confirm the HTTP page still settles. Then
            // restore full speed so later steps aren't penalized.
            var cdp = await Page.Context.NewCDPSessionAsync(Page);
            await cdp.SendAsync("Network.emulateNetworkConditions", new Dictionary<string, object>
            {
                ["offline"] = false,
                ["latency"] = 400,
                ["downloadThroughput"] = 50 * 1024,
                ["uploadThroughput"] = 50 * 1024,
            });
            await Page.GotoAsync("/http");
            await Expect(Page.Locator(".sample-result-body article.card")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 60_000 });

            if (opts.OfflineReconnect)
            {
                // Server only: on a high-latency link a handler round-trip should surface the
                // slow-link pending bar (it appears past the ~300ms threshold) and then clear
                // when the ack/render lands. 1.5s latency keeps the bar up long enough to assert
                // without racing the reply.
                await cdp.SendAsync("Network.emulateNetworkConditions", new Dictionary<string, object>
                {
                    ["offline"] = false,
                    ["latency"] = 1500,
                    ["downloadThroughput"] = 50 * 1024,
                    ["uploadThroughput"] = 50 * 1024,
                });
                await Page.GotoAsync("/events");
                var bump = Page.Locator(".sample-result-body button:has-text('Clicks:')").First;
                await Expect(bump).ToBeVisibleAsync(
                    new LocatorAssertionsToBeVisibleOptions { Timeout = 60_000 });
                await bump.ClickAsync();
                await Expect(Page.Locator(".rask-pending[data-show]")).ToBeVisibleAsync(
                    new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
                await Expect(Page.Locator(".rask-pending[data-show]")).ToBeHiddenAsync(
                    new LocatorAssertionsToBeHiddenOptions { Timeout = 15_000 });
            }
            else
            {
                // WASM only: the boot shell must carry the download-progress markup that main.js
                // drives via onDownloadResourceProgress. A full throttled re-boot is impractical
                // (multi-MB at 50 KB/s), so assert the shipped shell rather than the live fill.
                var shell = await Page.APIRequest.GetAsync("/index.html");
                Assert.Contains("rask-boot__progress", await shell.TextAsync());
            }

            await cdp.SendAsync("Network.emulateNetworkConditions", new Dictionary<string, object>
            {
                ["offline"] = false,
                ["latency"] = 0,
                ["downloadThroughput"] = -1,
                ["uploadThroughput"] = -1,
            });
        }

        if (opts.OfflineReconnect)
        {
            // Drop and restore the WebSocket; server-held state must survive the reconnect.
            await Page.GotoAsync("/events");
            await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Events",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
            var clicks = Page.Locator(".sample-result-body button:has-text('Clicks:')").First;
            await clicks.ClickAsync();
            await clicks.ClickAsync();
            await Expect(clicks).ToContainTextAsync("Clicks: 2",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
            await Page.Context.SetOfflineAsync(true);
            await Page.Context.SetOfflineAsync(false);
            await clicks.ClickAsync();
            await Expect(clicks).ToContainTextAsync("Clicks: 3",
                new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        }

        // Memory: a stress loop of in-SPA navigations must not balloon the JS heap.
        var baseline = await SampleJsHeapAsync();
        var labels = new[] { "Events", "Two-way binding", "Scoped CSS", "Routing", "Welcome" };
        for (var i = 0; i < 6; i++)
        {
            foreach (var label in labels)
            {
                await ClickSidebar(label);
                await Page.WaitForTimeoutAsync(120);
            }
        }
        await Page.EvaluateAsync("() => new Promise(r => { if (window.gc) { window.gc(); } setTimeout(r, 200); })");
        var after = await SampleJsHeapAsync();
        Assert.True(after > 0, $"no heap reading. baseline={baseline} after={after}");
        Assert.True(after < (baseline * 3) + 25_000_000 && after < 250_000_000,
            $"JS heap grew unexpectedly. baseline={baseline:N0} after={after:N0}.");

        await AssertNavigationScrollAsync();
    }

    // Scroll behaviour on forward navigation. The runtime resets window scroll to the top on a
    // "push" (a sidebar Navigator.NavigateTo or a data-rask-nav link click), and when the link
    // carried a "#fragment" it scrolls to that element instead. Both transports share the JS
    // runtime path (rask.js / rask.wasm.js applyNavScroll), so every host exercises it here.
    private async Task AssertNavigationScrollAsync()
    {
        // --- a forward nav resets scroll to the top ---------------------------------------------
        // The data table at 25 rows is reliably taller than the viewport; scroll to the bottom and
        // confirm the document actually moved before navigating away.
        await SideAsync("Data table", "Data table");
        await Page.Locator("select.form-select-sm").SelectOptionAsync("25");
        await Expect(Page.Locator("tbody tr")).ToHaveCountAsync(25,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Page.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");
        await Page.WaitForFunctionAsync("() => window.scrollY > 0",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        await SideAsync("Two-way binding", "Two-way binding");
        // The new page must land at the top (the reset can lag a CSS-deferred body commit, so poll).
        await Page.WaitForFunctionAsync("() => Math.round(window.scrollY) === 0",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // --- a data-rask-nav link with a #fragment scrolls to that element ----------------------
        // The showcase navigates via sidebar buttons, so inject a real NavLink-style anchor to drive
        // the click-interceptor + fragment path. /validation is a long page and #v7-product sits well
        // below the fold, so reaching it must move the scroll.
        await SideAsync("Welcome", "The Rask framework", "h1.display-5");
        await Page.EvaluateAsync(@"() => {
            const a = document.createElement('a');
            a.id = '__rask_anchor_probe';
            a.setAttribute('data-rask-nav', '');
            a.setAttribute('href', '/validation#v7-product');
            a.textContent = 'probe';
            document.querySelector('main').appendChild(a);
        }");
        await Page.Locator("#__rask_anchor_probe").ClickAsync();
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Validation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        // The fragment is preserved in the pushed URL (it never reaches the server, so the client
        // re-appends it) …
        await Expect(Page).ToHaveURLAsync(new Regex(".*/validation#v7-product$"),
            new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
        // … and the target is scrolled into view (top within the viewport) with the page actually
        // moved to get there (proving it was below the fold, not a no-op).
        await Page.WaitForFunctionAsync(@"() => {
            const el = document.getElementById('v7-product');
            if (!el) return false;
            const r = el.getBoundingClientRect();
            return window.scrollY > 0 && r.top >= -2 && r.top < window.innerHeight;
        }", null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    // ---- low-level helpers (shared by the journey) ---------------------------------------------

    private static int ExtractRenderCount(string? text) =>
        int.Parse(Regex.Match(text ?? "0", @"\d+").Value);

    // The #metrics-tick badge reads "tick N" — N is the background feed's tick count.
    private async Task<int> ReadMetricsTickAsync() =>
        ExtractRenderCount(await Page.Locator("#metrics-tick").TextContentAsync());

    private async Task HtmlDragDropAsync(string sourceSelector, string targetSelector)
    {
        var source = Page.Locator(sourceSelector);
        var target = Page.Locator(targetSelector);
        await source.ScrollIntoViewIfNeededAsync();
        var dataTransfer = await Page.EvaluateHandleAsync("() => new DataTransfer()");
        var init = new Dictionary<string, object>
        {
            ["dataTransfer"] = dataTransfer,
            ["bubbles"] = true,
            ["cancelable"] = true,
        };
        await source.DispatchEventAsync("dragstart", init);
        await target.DispatchEventAsync("dragover", init);
        await target.DispatchEventAsync("drop", init);
        await source.DispatchEventAsync("dragend", init);
    }

    private async Task ClearJsRuntimeStorageAsync()
    {
        try
        {
            await Page.EvaluateAsync(
                "() => { try { sessionStorage.removeItem('rask.jsruntime.demo'); } catch (_) {} }");
        }
        catch
        {
            // No page loaded yet — ignore.
        }
    }

    private async Task WaitForHighlightedSpansAsync(int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var settled = await Page.EvaluateAsync<bool>(
                "() => { const all = Array.from(document.querySelectorAll('pre code[class*=\"language-\"]')); " +
                "return all.length > 0 && all.every(c => c.querySelector('span[class]') !== null); }");
            if (settled)
            {
                return;
            }

            await Task.Delay(150);
        }
    }

    private Task<long> SampleJsHeapAsync() => Page.EvaluateAsync<long>(
        "() => (performance.memory && performance.memory.usedJSHeapSize) || 0");
}
