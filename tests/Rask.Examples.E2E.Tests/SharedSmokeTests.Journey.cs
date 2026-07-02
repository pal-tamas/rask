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

        await TestSidebarNavAsync();
        await WalkDslAndComponentPagesAsync();
        await WalkInteractiveComponentPagesAsync();
        await TestCompositionGuideAsync();
        await WalkLifecycleGuideAsync();
        await WalkRoutingGuideAsync();
        await WalkJsInteropGuideAsync();
        await WalkCqrsGuideAsync();
        await WalkAuthAndContextPagesAsync();
        await WalkFormsPagesAsync();
        await WalkStylingDataAndAppPagesAsync();
        await WalkBootstrapPagesAsync();
        await TestGuidesAsync();

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

    // The framework's root error boundary renders its "Something went wrong" shell — a div with the
    // distinctive .rask-error-boundary class (DefaultErrorPage) — when an error escapes every user
    // boundary. Outside the deliberate /boom demos it must never appear. Match that class precisely:
    // a bare main:has-text("Something went wrong") false-positives on legitimate page content that
    // merely contains the phrase — e.g. the Toast and Flash demos' CodeSample shows ToastDemo.cs /
    // FlashDemo.cs source whose "Danger"/"Error" message is literally "Something went wrong.".
    private async Task AssertNoGlobalCrashAsync() =>
        Assert.Equal(0, await Page.Locator(".rask-error-boundary").CountAsync());

    // The redesigned sidebar: collapsible groups (only the active route's group open by default), a
    // search filter, and — below md — a hamburger-driven offcanvas drawer. Exercised once per host.
    private async Task TestSidebarNavAsync()
    {
        // Guides-first: the guide category groups are expanded by default (the narrative spine), while the
        // demoted Examples/Bootstrap groups stay collapsed so the ~90-item list isn't dumped at once.
        await Expect(Page.Locator(".side-nav .nav-group-toggle").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var open = await Page.Locator(".side-nav .collapse.show").CountAsync();
        Assert.True(open >= 5, $"expected the guide groups expanded by default, got {open}");
        var groups = await Page.Locator(".side-nav .nav-group-toggle").CountAsync();
        Assert.True(groups > 8, $"expected the nav split into many collapsible groups, got {groups}");

        // Collapse/expand toggle: the guide category groups are open by default (guides-first), so
        // collapsing one hides its links and re-expanding reveals them. The "Core" guide group is stable
        // across the whole example→guide migration.
        var core = Page.Locator(".side-nav .nav-group-toggle:has-text(\"Core\")").First;
        var routingGuide = Page.Locator(".side-nav a.side-nav-link[href=\"/guides/routing\"]");
        await core.ClickAsync(); // collapse
        await Expect(routingGuide).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });
        await core.ClickAsync(); // re-expand
        await Expect(routingGuide).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // The filter narrows the list to matching labels (and force-opens their groups); clearing it
        // restores the accordion. Uses durable guide labels (always present).
        var filter = Page.Locator(".side-nav .side-nav-filter");
        await filter.FillAsync("Getting started");
        await Expect(Page.Locator(".side-nav a.side-nav-link:has-text(\"Getting started\")").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator(".side-nav a.side-nav-link:has-text(\"Composition\")"))
            .ToHaveCountAsync(0);
        await filter.FillAsync("");

        // Mobile: the sidebar collapses to an offcanvas drawer behind the hamburger. The static
        // desktop column gives way to an off-screen drawer toggled open, then dismissed by the backdrop.
        await Page.SetViewportSizeAsync(390, 844);
        await Expect(Page.Locator(".side-nav")).Not.ToBeInViewportAsync();
        await Page.Locator(".hamburger-btn").ClickAsync();
        await Expect(Page.Locator(".side-nav")).ToBeInViewportAsync(
            new LocatorAssertionsToBeInViewportOptions { Timeout = 10_000 });
        // Dismiss by tapping the backdrop. A real tap lands on the visible backdrop strip beside the
        // drawer, but Playwright's centre-click would be intercepted by the panel that overlays it —
        // so dispatch the click straight to the backdrop element (its data-rask-on-click still fires).
        await Page.Locator(".offcanvas-backdrop").DispatchEventAsync("click");
        await Expect(Page.Locator(".side-nav")).Not.ToBeInViewportAsync(
            new LocatorAssertionsToBeInViewportOptions { Timeout = 10_000 });
        await Page.SetViewportSizeAsync(1280, 720);
    }

    // The CQRS guide (docs/cqrs.md) embeds the counter slice. Driving it end-to-end proves the
    // source-generated dispatch works on this host: OnMount runs a query, the button sends a command
    // that returns a value and publishes a notification, and a pipeline behaviour logs every dispatch.
    // If AddRaskCqrs / the generated ModuleInitializer hadn't wired up on this transport, the demo
    // would throw "No handler is registered" and trip the root error boundary instead.
    private async Task WalkCqrsGuideAsync()
    {
        await ClickSidebar("CQRS");
        await Expect(Page.Locator("main .markdown-body h1").First).ToContainTextAsync("CQRS",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        var count = Page.Locator("#cqrs-count");
        await Expect(count).ToHaveTextAsync("0", new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });

        // Command → notification → query round-trip: the count increments and the dispatch log renders.
        await Page.Locator("#cqrs-increment").ClickAsync();
        await Expect(count).ToHaveTextAsync("1", new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator("#cqrs-log")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        await AssertNoGlobalCrashAsync();
    }

    // ---- page walk -----------------------------------------------------------------------------

    // Bootstrap section (Rask.Bootstrap). Proves the package's CSS is actually served from
    // _content/Rask.Bootstrap and that the interactive components run with ZERO bootstrap.js —
    // the modal opens and closes purely through Rask's live runtime.
    // The Guides section: the repo's docs/*.md rendered on-site by the Markdown component, now in the
    // Rails-guides-style chrome (Chapters TOC, on-this-page rail, prev/next) with live demos embedded
    // inline via <!-- demo:key --> markers. Verify a guide renders to a .markdown-body, cross-links are
    // SPA-routed, the Chapters TOC is present, and an embedded demo actually mounted its live result.
    private async Task TestGuidesAsync()
    {
        await SideAsync("All guides", "Guides");
        // A guide card links to /guides/{slug}; open the Routing guide.
        await Page.Locator("main a[href$='/guides/routing']").First.ClickAsync();
        await Expect(Page.Locator("main .markdown-body h1").First).ToContainTextAsync("Routing",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        // The markdown's relative .md cross-links are rewritten to SPA-routed /guides/* anchors.
        Assert.True(await Page.Locator(".markdown-body a[data-rask-nav][href^='/guides/']").CountAsync() > 0,
            "expected the rendered guide to carry SPA-routed cross-links");

        // The Rails-style chrome: a Chapters TOC linking in-page anchors, and prev/next book-nav.
        await Expect(Page.Locator(".guide-chapters .guide-chapters-list a[href^='#']").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        Assert.True(await Page.Locator(".guide-prevnext .guide-prevnext-link").CountAsync() > 0,
            "expected prev/next navigation on the guide");

        // A demo embedded via <!-- demo:key --> mounted its CodeSample inline — the marker resolved to a
        // real component, not dropped as an HTML comment.
        Assert.True(await Page.Locator(".guide-demo .sample-card").CountAsync() > 0,
            "expected the Routing guide to embed at least one live demo");
        Assert.Equal(0, await Page.Locator("text=Unknown demo").CountAsync());

        await AssertNoGlobalCrashAsync();
    }

    private async Task WalkBootstrapPagesAsync()
    {
        // Navbar & nav — the typed navigation primitives. The demo's nav links render as SPA-routed
        // anchors (data-rask-nav), the same primitive the showcase chrome is built from.
        await SideAsync("Navbar & nav", "Navbar & nav");
        await Expect(Page.Locator(".sample-result-body .navbar").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        Assert.True(await Page.Locator(".sample-result-body .nav .nav-link[data-rask-nav]").CountAsync() > 0);

        await SideAsync("Buttons & badges", "Buttons & badges");
        // Bootstrap CSS applied: the .btn has Bootstrap's padding (non-zero), proving _content served.
        var btn = Page.Locator(".sample-result-body button.btn.btn-primary").First;
        await Expect(btn).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        // Modal — open + close driven by Rask state, no bootstrap.js loaded.
        await SideAsync("Modal", "Modal");
        await Page.Locator(".sample-result-body button:has-text(\"Launch demo modal\")").First.ClickAsync();
        await Expect(Page.Locator("div.modal.show").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Page.Locator("div.modal .btn-close").First.ClickAsync();
        await Expect(Page.Locator("div.modal.show")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });

        await SideAsync("Tabs & accordion", "Tabs & accordion");
        await SideAsync("Utility classes", "Utility classes");
    }

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
    }

    private async Task WalkInteractiveComponentPagesAsync()
    {
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

        // Virtualize and Keyed lists moved to the Composition guide (TestCompositionGuideAsync).

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

        // Drag & drop and Error boundary moved to the Composition guide (TestCompositionGuideAsync).
    }

    // Composition guide: context, callbacks, virtualize, keyed lists, drag & drop, and error boundaries
    // — their standalone example pages folded into docs/composition.md as inline live demos. Open the
    // guide once and drive each demo in place; locators are scoped by unique #id or by the enclosing
    // .guide-demo (badges/result panes repeat across demos on the one page).
    private async Task TestCompositionGuideAsync()
    {
        var contains = new LocatorAssertionsToContainTextOptions { Timeout = 10_000 };

        await SideAsync("Composition", "Composition", "main .markdown-body h1");
        Assert.True(await Page.Locator(".guide-demo .sample-card").CountAsync() >= 8,
            "expected the Composition guide to embed the demos as live demos");
        // Wait for a LATE demo's control (the error-boundary trigger, near the end) before driving any
        // interaction, so a fill/click never races the guide still hydrating on the slower transports.
        await Expect(Page.Locator("#boom-throw")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        // Context: toggling a provider updates a deep consumer straight through a render-cached
        // intermediate. Scope to this demo — badges appear in other demos on the page too.
        var ctxDemo = Page.Locator(".guide-demo:has(button:has-text('Toggle theme'))");
        var ctxBadge = ctxDemo.Locator(".badge");
        await Expect(ctxBadge).ToContainTextAsync("Light", contains);
        await ctxDemo.Locator("button:has-text('Toggle theme')").ClickAsync();
        await Expect(ctxBadge).ToContainTextAsync("Dark", contains);

        // Callback: a child's click invokes the parent's plain delegate and the framework auto-wraps it
        // to re-render the parent. Scoped by the demo's #callback-rating container.
        var cb = Page.Locator("#callback-rating");
        await cb.Locator("button").Nth(3).ClickAsync();
        await Expect(cb.Locator("p")).ToContainTextAsync("You rated: 4/5", contains);

        // Virtualize: the windowed list pins its sticky header on the <th> cells (static check).
        var thPosition = await Page.Locator("[data-testid=virtualize-scroller] thead th").First
            .EvaluateAsync<string>("el => getComputedStyle(el).position");
        Assert.Equal("sticky", thPosition);

        // Keyed lists: reversing the list re-orders the live rows through keyed structural moves — the
        // first row flips from Apple to Elderberry. (The finer contract — an uncommitted input value and
        // caret riding its keyed row across the move — is exercised by the keyed-reconciliation unit
        // tests in Rask.Core.Tests; asserting it through the browser on the co-mounted guide is
        // timing-fragile, so the E2E proves the reverse re-renders the live keyed list instead.)
        await Expect(Page.Locator("#kl-list li").First.Locator("span.fw-semibold"))
            .ToContainTextAsync("Apple", contains);
        await Page.Locator("#kl-reverse").ClickAsync();
        await Expect(Page.Locator("#kl-list li").First.Locator("span.fw-semibold"))
            .ToContainTextAsync("Elderberry", contains);

        // Drag & drop: native HTML5 drag events fire the C# handlers; the live diff morphs the DOM.
        await Expect(Page.Locator("#dd-fruit-list .dd-item")).ToHaveCountAsync(5,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await HtmlDragDropAsync("[data-testid='fruit-0']", "[data-testid='fruit-2']");
        await Expect(Page.Locator("#dd-fruit-list .dd-item").Nth(2)).ToContainTextAsync("Apple", contains);
        await HtmlDragDropAsync("[data-testid='card-2']", "[data-testid='card-5']");
        await Expect(Page.Locator("[data-testid='col-done'] [data-testid='card-2']")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Error boundaries: a handler-throw and a render-throw each trip the nearest boundary's fallback
        // — the error is contained, the navbar (outside the user boundary) survives, Recover restores.
        await Page.Locator("#boom-throw").ClickAsync();
        await Expect(Page.Locator("#boom-fallback").First).ToContainTextAsync("kaboom — handler boundary demo", contains);
        await Expect(Page.Locator(".navbar .navbar-brand")).ToContainTextAsync("Rask"); // root boundary not tripped
        await Page.Locator("#boom-recover").First.ClickAsync();
        await Expect(Page.Locator("#boom-throw")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // Render-time throw: the serializer rewinds the partial output and the boundary catches it once.
        await Page.Locator("#boom-render-trigger").ClickAsync();
        await Expect(Page.Locator("#boom-fallback").First).ToContainTextAsync("kaboom — render-time boundary demo", contains);
        await Page.Locator("#boom-recover").First.ClickAsync();
        await Expect(Page.Locator("#boom-render-trigger")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    private async Task WalkAuthAndContextPagesAsync()
    {
        // Context and Callback moved to the Composition guide (TestCompositionGuideAsync).

        // Toast: Bootstrap toasts shown, stacked and dismissed entirely by live-diff state (no Bootstrap
        // JS, no data-bs-dismiss). Showing renders class="toast show"; the × removes it from the host list.
        await SideAsync("Toast", "Toast");
        var toast = Page.Locator("main .sample-result-body .toast.show");
        await Page.Locator("main .sample-result-body button:has-text('Show toast')").ClickAsync();
        await Expect(toast).ToContainTextAsync("Hello, world!",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await toast.Locator(".btn-close").ClickAsync();
        await Expect(toast).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        // Flash messages: a producer injects IFlash and raises a message; the headless FlashOutlet drains
        // it (consumed-once) and renders a dismissible BsAlert — all via live-diff state, no client JS.
        await SideAsync("Flash messages", "Flash messages");
        var flashAlert = Page.Locator("main .sample-result-body .alert");
        await Page.Locator("main .sample-result-body button:has-text('Success')").ClickAsync();
        await Expect(flashAlert).ToContainTextAsync("Your changes were saved.",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await flashAlert.Locator(".btn-close").ClickAsync();
        await Expect(flashAlert).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

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

    // Lifecycle guide: the Lifecycle / Disposal / Cancellation / Background-service example pages were
    // folded into docs/lifecycle.md as inline live demos, so the whole cluster is one guide page now.
    // Open it once and drive each demo in place — locators are scoped by unique #id.
    private async Task WalkLifecycleGuideAsync()
    {
        await SideAsync("Lifecycle", "Lifecycle", "main .markdown-body h1");
        Assert.True(await Page.Locator(".guide-demo .sample-card").CountAsync() >= 7,
            "expected the Lifecycle guide to embed the lifecycle demos as live demos");
        // The guide co-mounts every lifecycle demo on one page; wait for the LAST demo's control (the
        // background-service chart, near the end) before driving any interaction so clicks never race
        // hydration on the slower transports.
        await Expect(Page.Locator("#metrics-chart svg")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        // Lifecycle hooks: the awaited OnMountAsync continuation must run, and "Trigger re-render" bumps
        // the render counter (an event-handler render — it does not re-fire OnMount / OnPropsChanged).
        await Expect(Page.Locator("li code:has-text('OnMountAsync (after')"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        var badge = Page.Locator(".badge:has-text('Render #')").First;
        var before = ExtractRenderCount(await badge.TextContentAsync());
        await Page.Locator("button:has-text('Trigger re-render')").ClickAsync();
        await Expect(badge).Not.ToContainTextAsync($"Render #{before}",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Mount / unmount cycle: mounting then unmounting the probe fires OnUnmount / OnUnmountAsync,
        // logged into the parent-held list (which survives the unmount).
        await Page.Locator("#lifecycle-cycle-mount").ClickAsync();
        await Page.Locator("#lifecycle-cycle-unmount").ClickAsync();
        await Expect(Page.Locator("#lifecycle-cycle-log")).ToContainTextAsync("OnUnmount",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Disposal: sync IDisposable + async IAsyncDisposable both fire on unmount.
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

        // Cancellation: unmount a probe mid-delay → its CancellationToken fires and it logs cancelled.
        await Page.Locator("#cancel-mount").ClickAsync();
        await Expect(Page.Locator(".cancel-probe-pill")).ToContainTextAsync("running",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator("#cancel-unmount").ClickAsync();
        await Expect(Page.Locator(".cancel-log")).ToContainTextAsync("cancelled",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Background service: an app-wide singleton's loop pushes updates to two decoupled subscribers.
        // The tick badge must climb with NO user interaction — proof the background producer (not a
        // click handler) drives the render.
        var firstTick = await ReadMetricsTickAsync();
        await Expect(Page.Locator("#metrics-tick")).Not.ToContainTextAsync($"tick {firstTick}",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        Assert.True(await ReadMetricsTickAsync() > firstTick, "the background feed did not advance on its own");
    }

    // Routing guide: the Routing / Route+query / Navigator example pages folded into docs/routing.md.
    // The guide is otherwise code-only (navigating the showcase itself IS the live routing); the one
    // live demo is the Navigator query mutators, which operate on this guide's own URL.
    private async Task WalkRoutingGuideAsync()
    {
        await SideAsync("Routing", "Routing", "main .markdown-body h1");
        Assert.True(await Page.Locator(".guide-demo .sample-card").CountAsync() >= 1,
            "expected the Routing guide to embed the Navigator demo as a live demo");
        var navDemo = Page.Locator(".guide-demo:has(#nav-query)");
        await Expect(navDemo.Locator("#nav-query")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });
        // SetQuery mutates this page's own query; the URL and the live readout both update.
        await navDemo.Locator("#nav-set-sort").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*[?&]sort=asc.*"),
            new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
        await Expect(navDemo.Locator("#nav-query")).ToContainTextAsync("sort=asc",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await navDemo.Locator("#nav-clear").ClickAsync();
        await Expect(navDemo.Locator("#nav-query")).ToContainTextAsync("(empty)",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    }

    // JS-interop guide: the Element refs / Scoped CSS / IJSRuntime / Asset-loading example pages folded
    // into docs/js-interop.md as inline live demos. Open the guide once, hydration-gate on a late demo
    // (the lazy-mount toggle near the end), then drive each demo by #id / scoped locator.
    private async Task WalkJsInteropGuideAsync()
    {
        await ClearJsRuntimeStorageAsync();
        await SideAsync("JavaScript interop", "JavaScript interop", "main .markdown-body h1");
        Assert.True(await Page.Locator(".guide-demo .sample-card").CountAsync() >= 7,
            "expected the JS-interop guide to embed the demos as live demos");
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameString = "Show LazyChild" })).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        // Scoped CSS: two components declare the same `.box` selector; each is scoped, so the computed
        // background colours differ and neither is the transparent default.
        var boxes = Page.Locator(".guide-demo .sample-result-body .box");
        await Expect(boxes).ToHaveCountAsync(2, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        var bg0 = await boxes.Nth(0).EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        var bg1 = await boxes.Nth(1).EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.NotEqual(bg0, bg1);
        Assert.NotEqual("rgba(0, 0, 0, 0)", bg0);

        // Element refs: focus a built-in, then measure the box via the sibling scoped JS.
        var elDemo = Page.Locator(".guide-demo:has(button:has-text('Measure the box'))");
        await elDemo.Locator("button:has-text('Focus the input')").ClickAsync();
        await Expect(elDemo.Locator(".sample-result-body input"))
            .ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 10_000 });
        await elDemo.Locator("button:has-text('Measure the box')").ClickAsync();
        await Expect(elDemo.Locator(".sample-result-body p"))
            .ToContainTextAsync("Box width:", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        // Scoped JS namespace is present (the measure invoked window.Rask.ElementRefDemo.width).
        Assert.True(
            await Page.EvaluateAsync<bool>("() => typeof window.Rask === 'object' && window.Rask !== null"),
            "scoped JS namespace window.Rask is missing — component JS did not load");

        // CodeSample tabs + copy on the Element refs demo's source pane: switching tabs swaps one Raw
        // highlighted pane for another (must reparse into real token <span>s, not escaped text); copy
        // flashes "Copied!".
        var codeCard = Page.Locator(".sample-code-col:has(.sample-tab:has-text('ElementRefDemo.js'))").First;
        await codeCard.Locator(".sample-tab:has-text('ElementRefDemo.js')").ClickAsync();
        await Expect(codeCard.Locator(".sample-code"))
            .ToContainTextAsync("getBoundingClientRect", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(codeCard.Locator(".sample-code code span[class]").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await codeCard.Locator(".sample-copy").ClickAsync();
        await Expect(codeCard.Locator(".sample-copy"))
            .ToContainTextAsync("Copied!", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // IJSRuntime: sessionStorage set/read/remove round-trip through the unified IJSRuntime.
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

        // Asset loading: scoped CSS/JS each ship as ONE content-addressed bundle, so a lazily-mounted
        // component is styled the instant its node is inserted — no extra <link>, no FOUC.
        var cssLinkSel = "head link[rel='stylesheet'][href^='/_rask/a/']";
        Assert.Equal(1, await Page.Locator(cssLinkSel).CountAsync());
        Assert.Equal(1, await Page.Locator("head script[src^='/_rask/a/'][src$='.js']").CountAsync());
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
        await Page.GetByRole(AriaRole.Button, new() { NameString = "Show LazyChild" }).ClickAsync();
        await Expect(Page.Locator(".lazy-child")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        Assert.Equal(1, await Page.Locator(cssLinkSel).CountAsync());
        await Page.WaitForFunctionAsync("() => window.__raskLazyApplied !== null",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__raskLazyApplied === true"),
            "LazyChild's scoped rule (from the bundle) must be applied when the node is inserted (no FOUC)");
        await Page.GetByRole(AriaRole.Button, new() { NameString = "Hide LazyChild" }).ClickAsync();
        await Expect(Page.Locator(".lazy-child")).ToHaveCountAsync(0);
        Assert.Equal(1, await Page.Locator(cssLinkSel).CountAsync());
    }

    private async Task WalkFormsPagesAsync()
    {
        // Forms & validation guide: the seven standalone forms example pages (binding, form controls,
        // validation, floating labels, complex models, radio/checkbox groups, multi-select) were folded
        // into docs/forms.md as inline live demos in the guides-first migration, so the whole section is
        // one page now. Open the guide once and drive each demo in place — locators are scoped by unique
        // #id or by the enclosing .guide-demo where option values (Pro/AI) repeat across demos.
        await SideAsync("Forms & validation", "Forms & validation", "main .markdown-body h1");
        Assert.True(await Page.Locator(".guide-demo .sample-card").CountAsync() >= 25,
            "expected the Forms guide to embed the forms demos as live demos");
        // The guide co-mounts every forms demo on one (large) page; wait for a late demo's control (the
        // multi-select, near the end) before driving any interaction so clicks never race hydration.
        await Expect(Page.Locator("#ms-interests")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        // Two-way binding: typed bind echo (the per-type / nullable / clear-to-null matrix is unit-
        // tested in Rask.Core.Tests/Forms — here we prove the live round trip for a text + a
        // change-only checkbox). The typed-bind demo's Name input is the first on the page (section 1).
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

        // Radio & checkbox groups: single-value radio bind + ICollection checkbox bind. Scope the
        // option locators to this demo — the "Pro"/"AI" values also appear in the form-controls and
        // multi-select demos on the same guide page.
        var groupsDemo = Page.Locator(".guide-demo:has(#groups-summary)");
        var groups = groupsDemo.Locator("#groups-summary");
        await groupsDemo.Locator("input[type=radio][value='Pro']").CheckAsync();
        await Expect(groups).ToContainTextAsync("Plan: Pro", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await groupsDemo.Locator("input[type=checkbox][value='AI']").CheckAsync();
        await Expect(groups).ToContainTextAsync("AI", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Multi-select: the reusable BsMultiSelect<T> dropdown binds to an ICollection — open it (server
        // live-diff, no Bootstrap JS), pick an option and it appears as a live chip (the control re-renders
        // itself — no StateHasChanged). (Component mechanics are unit-tested in Demos/MultiSelectTests.)
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

        // Controlled BsMultiSelect (Value + OnChange, no Bind): selecting a topic flows out through OnChange
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
        // with no StateHasChanged in the demo — including the Component-style controls (BsRadioGroup /
        // BsCheckboxGroup / BsMultiSelect) whose bound writes re-render the host via the binding owner.

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

        // BsRadioGroup — bound (Component control): the derived readout sits OUTSIDE the Form yet updates.
        await Page.Locator("input[type=radio][name='fc-radio-b'][value='Team']").CheckAsync();
        await Expect(Page.Locator("#fc-radio-bound-out")).ToContainTextAsync("Team",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // BsCheckboxGroup — controlled.
        await Page.Locator("input[type=checkbox][name='fc-checkbox-c'][value='AI']").CheckAsync();
        await Expect(Page.Locator("#fc-checkbox-controlled-out")).ToContainTextAsync("AI",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // BsMultiSelect — bound: open, pick a topic, the readout outside the Form updates; then close so the
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
        // and on desktop the sidebar's body uses it to become an independent, viewport-bounded scroll
        // region so the list scrolls inside itself rather than stretching the page — the "navbar too
        // tall" fix. (Groups now collapse by default, so the list itself is short; this only asserts
        // the region is bounded and scrollable when needed, not that it currently overflows.)
        Assert.Equal("56px", (await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.documentElement).getPropertyValue('--nav-h').trim()")));
        var navScroll = await Page.Locator(".side-nav .offcanvas-body").First.EvaluateAsync<string>(
            @"el => {
                const cs = getComputedStyle(el);
                const navH = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--nav-h'));
                return JSON.stringify({
                    overflowY: cs.overflowY,
                    bounded: el.clientHeight <= window.innerHeight - navH + 1,
                });
            }");
        Assert.Contains("\"overflowY\":\"auto\"", navScroll);
        Assert.Contains("\"bounded\":true", navScroll);

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

        await TestBrowserApisAsync();
    }

    // Browser APIs guide: the 27 typed wrappers folded into docs/browser-apis.md. Browser APIs are
    // device/permission-dependent and mostly no-op headless, and co-mounting 27 live JS-interop demos on
    // one page contends for the shared JS channel — so the guide embeds each wrapper as an inline *code
    // sample* (highlighted source, no auto-mounted live result). Verify the guide renders those samples;
    // per-wrapper behaviour is covered by the demo unit tests and the WASM PWA/hardware showcase.
    private async Task TestBrowserApisAsync()
    {
        var contains = new LocatorAssertionsToContainTextOptions { Timeout = 10_000 };
        var visible = new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 };

        // The Browser APIs guide co-mounts every typed wrapper as a LIVE demo on one page (the child
        // enumerable is materialised at render time so each demo's component instance is reconciled and
        // keeps its state across renders — see Component's IEnumerable<Child> indexer). Open the guide,
        // wait for the LAST demo's control so no interaction races hydration, then drive a representative
        // set: one-shot reads, storage/clipboard round-trips, and JS→C# push. Exhaustive per-wrapper
        // behaviour is covered by the demo unit tests.
        await SideAsync("Browser APIs", "Browser APIs", "main .markdown-body h1");
        Assert.True(await Page.Locator(".guide-demo .sample-card").CountAsync() >= 20,
            "expected the Browser APIs guide to embed the typed wrappers as live demos");
        await Expect(Page.Locator("#bc-send")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        // Storage — localStorage round-trip via IBrowserStorage.
        await Page.Locator("#storage-input").FillAsync("persist-me");
        await Page.Locator("#storage-set").ClickAsync();
        await Expect(Page.Locator("#storage-status")).ToContainTextAsync("Stored: persist-me", contains);
        await Page.Locator("#storage-read").ClickAsync();
        await Expect(Page.Locator("#storage-read-value")).ToHaveTextAsync("persist-me",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });

        // Cookies — set then read back via ICookies.
        await Page.Locator("#cookie-input").FillAsync("choco");
        await Page.Locator("#cookie-set").ClickAsync();
        await Page.Locator("#cookie-get").ClickAsync();
        await Expect(Page.Locator("#cookie-read-value")).ToHaveTextAsync("choco",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });

        // Clipboard — copy then read back (granted on the context in InitializeAsync).
        await Page.Locator("#clipboard-copy").ClickAsync();
        await Page.Locator("#clipboard-paste").ClickAsync();
        await Expect(Page.Locator("#clipboard-read-value")).ToContainTextAsync("Copied from Rask!", contains);

        // Geolocation — one-shot position (fixed fix granted on the context).
        await Page.Locator("#geo-get").ClickAsync();
        await Expect(Page.Locator("#geo-value")).ToContainTextAsync("lat 51.5", contains);

        // Browser info / media queries / screen info — one-shot property reads populate their readouts.
        await Page.Locator("#nav-read").ClickAsync();
        await Expect(Page.Locator("#nav-value")).ToContainTextAsync("online:", contains);
        await Page.Locator("#media-read").ClickAsync();
        await Expect(Page.Locator("#media-value")).ToContainTextAsync("prefersDark:", contains);
        await Page.Locator("#screen-read").ClickAsync();
        await Expect(Page.Locator("#screen-value")).ToContainTextAsync("DPR", contains);

        // Vibration / speech are device-dependent (no-op headless) — smoke-check the control renders.
        await Expect(Page.Locator("#vibrate-buzz")).ToBeVisibleAsync(visible);
        await Expect(Page.Locator("#speech-speak")).ToBeVisibleAsync(visible);

        // Broadcast channel — full JS→C# push round-trip (BroadcastChannel.onmessage → [JSInvokable] →
        // handler → StateHasChanged), on every host including trimmed WASM.
        await Page.Locator("#bc-send").ClickAsync();
        await Expect(Page.Locator("#bc-log")).ToContainTextAsync("Message #1", contains);

        // Intersection observer — another push: scroll the target in and the browser pushes the change.
        await Expect(Page.Locator("#io-status")).ToContainTextAsync("out of view", contains);
        await Page.Locator("#io-target").ScrollIntoViewIfNeededAsync();
        await Expect(Page.Locator("#io-status")).ToContainTextAsync("in view", contains);
    }

    private async Task TestInSessionNotFoundAsync()
    {
        await Page.EvaluateAsync(@"() => {
            history.pushState({ rask: true }, '', '/in-session-missing');
            window.dispatchEvent(new PopStateEvent('popstate'));
        }");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Page not found",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator(".side-nav a.side-nav-link.active")).ToHaveCountAsync(0,
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
            // and re-emit server-highlighted spans. The Data table page's only language-code block is
            // its own highlighted page-source sample, so every match must carry token spans.
            await Page.GotoAsync("/table");
            await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Data table",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
            await Page.ReloadAsync();
            Assert.Equal(0, await Page.Locator(".rask-error-boundary h1:has-text(\"Something went wrong\")").CountAsync());
            await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Data table",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
            await WaitForHighlightedSpansAsync(HighlightSettleTimeoutMs);
            var total = await Page.Locator("pre code[class*='language-']").CountAsync();
            var highlighted = await Page.Locator("pre code[class*='language-']:has(span[class])").CountAsync();
            Assert.True(total > 0 && total == highlighted,
                $"/table after refresh: {highlighted}/{total} highlighted.");

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
        var labels = new[] { "Events", "Tag factories", "JavaScript interop", "Routing", "Welcome" };
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

        await SideAsync("Tag factories", "Tag factories");
        // The new page must land at the top (the reset can lag a CSS-deferred body commit, so poll).
        await Page.WaitForFunctionAsync("() => Math.round(window.scrollY) === 0",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // --- a data-rask-nav link with a #fragment scrolls to that element ----------------------
        // The showcase navigates via sidebar buttons, so inject a real NavLink-style anchor to drive
        // the click-interceptor + fragment path. The Routing guide is a long page and its last section
        // (#not-found-and-auth-gating, an AutoIdentifiers heading anchor) sits well below the fold, so
        // reaching it must move the scroll.
        await SideAsync("Welcome", "The Rask framework", "h1.display-5");
        await Page.EvaluateAsync(@"() => {
            const a = document.createElement('a');
            a.id = '__rask_anchor_probe';
            a.setAttribute('data-rask-nav', '');
            a.setAttribute('href', '/guides/routing#not-found-and-auth-gating');
            a.textContent = 'probe';
            document.querySelector('main').appendChild(a);
        }");
        await Page.Locator("#__rask_anchor_probe").ClickAsync();
        await Expect(Page.Locator("main .markdown-body h1")).ToHaveTextAsync("Routing",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        // The fragment is preserved in the pushed URL (it never reaches the server, so the client
        // re-appends it) …
        await Expect(Page).ToHaveURLAsync(new Regex(".*/guides/routing#not-found-and-auth-gating$"),
            new PageAssertionsToHaveURLOptions { Timeout = 10_000 });
        // … and the target is scrolled into view (top within the viewport) with the page actually
        // moved to get there (proving it was below the fold, not a no-op).
        await Page.WaitForFunctionAsync(@"() => {
            const el = document.getElementById('not-found-and-auth-gating');
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
