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
        // Guides-first: "/" is the guides index now (the Welcome landing page is gone); its PageHeader
        // renders an <h1 class="h2">Guides</h1>.
        await Expect(Page.Locator("main h1.h2"))
            .ToContainTextAsync("Guides",
                new LocatorAssertionsToContainTextOptions { Timeout = 60_000 });

        // Plant a sentinel on window — every in-SPA nav below must preserve it (proves no full
        // reload happened and the SPA context survived).
        await Page.EvaluateAsync("() => { window.__raskSentinel = 'alive'; }");

        // Theme toggle: the navbar toggle flips BOTH data-theme and data-bs-theme on <html> together (the
        // raw-token layer + the Bootstrap bridge stay in lockstep). Toggle, assert the flip, then toggle
        // back so the rest of the journey runs at the original theme.
        var themeToggle = Page.Locator("nav button:has(.bi-circle-half)").First;
        var themeBefore = await Page.EvaluateAsync<string>(
            "() => document.documentElement.getAttribute('data-bs-theme') || ''");
        await themeToggle.ClickAsync();
        await Page.WaitForFunctionAsync(
            "p => { const t = document.documentElement.getAttribute('data-bs-theme');"
            + " return t && t !== p && document.documentElement.getAttribute('data-theme') === t; }",
            themeBefore, new PageWaitForFunctionOptions { Timeout = 15_000 });
        await themeToggle.ClickAsync();
        await Page.WaitForFunctionAsync(
            "p => document.documentElement.getAttribute('data-bs-theme') === p",
            themeBefore, new PageWaitForFunctionOptions { Timeout = 15_000 });

        await TestSidebarNavAsync();
        await WalkUserComponentsGuideAsync();
        await TestCompositionGuideAsync();
        await WalkLifecycleGuideAsync();
        await WalkRoutingGuideAsync();
        await WalkJsInteropGuideAsync();
        await WalkElementsGuideAsync();
        await WalkHttpAndFilesGuideAsync();
        await WalkCqrsGuideAsync();
        await WalkAuthGuideAsync();
        await WalkFormsPagesAsync();
        await WalkStylingDataAndAppPagesAsync();
        await WalkBootstrapGuideAsync();
        await WalkDataGridGuideAsync();
        await TestGuidesAsync();

        await TestInSessionNotFoundAsync();

        // The SPA sentinel must have survived the entire in-SPA walk.
        Assert.Equal("alive", await Page.EvaluateAsync<string?>("() => window.__raskSentinel"));

        await AssertNoDuplicateScopedHeadLinksAsync();

        await RunUnusualActivityAsync(opts);
    }

    // ---- helpers -------------------------------------------------------------------------------

    // In-SPA navigation via the sidebar + heading assertion. Works on every host once the shell is
    // loaded; on StandaloneWasm the sidebar click is the only navigation path available.
    protected async Task SideAsync(string label, string heading, string headingSelector = "main h1.h2")
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
    // merely contains the phrase — e.g. the BsToast element and Toaster demos' CodeSample shows
    // ToastDemo.cs / ToasterDemo.cs source whose "Danger"/"Error" message is literally "Something went wrong.".
    protected async Task AssertNoGlobalCrashAsync() =>
        Assert.Equal(0, await Page.Locator(".rask-error-boundary").CountAsync());

    // After walking every page, each scoped component's keyed stylesheet (<link data-rask-key>) must
    // appear in <head> exactly once. On hosts that deliver scoped CSS per component via a full reply
    // (Server), the FOUC preload (rask-scoped.js clones the incoming keyed <link> before the morph)
    // must reconcile against the clone by key rather than duplicate it — a regression here silently
    // leaks one <link> per scoped component ever mounted and re-applies unmounted pages' CSS. This also
    // guards the foreign-head-preservation observer against wrongly tagging a keyed framework link.
    protected async Task AssertNoDuplicateScopedHeadLinksAsync()
    {
        var duplicateKeys = await Page.EvaluateAsync<string[]>(
            """
            () => {
                const counts = {};
                for (const l of document.querySelectorAll('head link[rel="stylesheet"][data-rask-key]')) {
                    const k = l.getAttribute('data-rask-key');
                    counts[k] = (counts[k] || 0) + 1;
                }
                return Object.keys(counts).filter(k => counts[k] > 1);
            }
            """);
        Assert.True(duplicateKeys.Length == 0,
            $"Duplicate scoped stylesheet <link>s in <head> (leaked): {string.Join(", ", duplicateKeys)}");
    }

    // The redesigned sidebar: collapsible groups (only the active route's group open by default), a
    // search filter, and — below md — a hamburger-driven offcanvas drawer. Exercised once per host.
    protected async Task TestSidebarNavAsync()
    {
        // Guides-first: the guide category groups are expanded by default (the narrative spine), while the
        // demoted Examples/Bootstrap groups stay collapsed so the ~90-item list isn't dumped at once.
        await Expect(Page.Locator(".side-nav .nav-group-toggle").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var open = await Page.Locator(".side-nav .collapse.show").CountAsync();
        Assert.True(open >= 5, $"expected the guide groups expanded by default, got {open}");
        var groups = await Page.Locator(".side-nav .nav-group-toggle").CountAsync();
        // The five guide groups (Overview + the four GuideCatalog categories) plus the surviving Examples
        // groups (most example pages are now folded into guides). Keep this a "many groups" floor.
        Assert.True(groups >= 6, $"expected the nav split into many collapsible groups, got {groups}");

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
    protected async Task WalkCqrsGuideAsync()
    {
        await ClickSidebar("CQRS");
        await Expect(Page.Locator("main .markdown-body h1").First).ToContainTextAsync("CQRS",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        // Forward navigation moves focus into the new page's <main> and announces the route via the
        // aria-live region, so keyboard/screen-reader users continue from the new page (not the stale
        // nav link) and hear the change. Server live-runtime only (rask.js) for now; the WASM host's
        // navigation path is a follow-up.
        if (FixtureName == "Server")
        {
            await Expect(Page.Locator("main.page-main")).ToBeFocusedAsync(
                new LocatorAssertionsToBeFocusedOptions { Timeout = 5_000 });
            await Expect(Page.Locator(".rask-route-announcer")).Not.ToBeEmptyAsync(
                new LocatorAssertionsToBeEmptyOptions { Timeout = 3_000 });
        }

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
    protected async Task TestGuidesAsync()
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

    // Bootstrap guide: the Rask.Bootstrap component showcase, now split into one small page per component
    // group under the "Bootstrap" sidebar group (a thin hub + buttons, cards, feedback, icons, navbar,
    // overlays, disclosure, toasts, form controls, selects, pickers, utilities). Walk the pages that carry
    // interactive demos, hydration-gate on one demo per page, and drive the representative components in
    // place — all live via Rask state, no bootstrap.js. Demo resolution is keyed by demo-id, so the demos
    // themselves are unchanged; only their host page moved.
    protected async Task WalkBootstrapGuideAsync()
    {
        // === Hub: the overview / component-map page (no live demos — it links out to each group page). ===
        await SideAsync("Bootstrap", "Rask.Bootstrap", "main .markdown-body h1");
        // The component map's relative .md links were rewritten to SPA-routed /guides/* anchors.
        Assert.True(
            await Page.Locator(".markdown-body a[data-rask-nav][href$='/guides/bootstrap-buttons']").CountAsync() > 0,
            "expected the Bootstrap hub to link out to the per-group pages");

        // === Buttons & badges — Bootstrap CSS applied (the _content bundle served): .btn-primary renders. ===
        await SideAsync("Buttons & badges", "buttons & badges", "main .markdown-body h1");
        Assert.True(await Page.Locator(".guide-demo .sample-card").CountAsync() >= 1,
            "expected the Buttons page to embed the buttons demo as a live demo");
        // Gate on it so the page has hydrated before we assert.
        await Expect(Page.Locator(".guide-demo .sample-result-body button.btn.btn-primary").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });
        // Badges render on the same demo (the .badge span; exact markup is unit-tested).
        await Expect(Page.Locator(".guide-demo .sample-result-body .badge").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // === Tabs, accordion & collapse — the disclosure components, all controlled and zero-JS. The
        //     accordion is embedded in the tabs demo; BsCollapse has its own demo whose toggle flips Open. ===
        await SideAsync("Tabs, accordion & collapse", "tabs, accordion & collapse", "main .markdown-body h1");
        await Expect(Page.Locator(".guide-demo .sample-result-body .accordion").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });
        // BsCollapse — its card body starts hidden (.collapse without .show → display:none). The toggle
        // adds .show through the live diff and reveals it, no bootstrap.js. Scope to .card-body so the
        // accordion's own .collapse panels (one starts open) don't match.
        var collapseBody = Page.Locator(".guide-demo .sample-result-body .collapse .card-body").First;
        await Expect(collapseBody).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });
        await Page.Locator(".guide-demo button:has-text('Show details')").First.ClickAsync();
        await Expect(collapseBody).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // === Alerts, spinners & progress — the feedback components. All render live; a structural
        //     visibility assertion is the browser-side proof, their exact markup is unit-tested. ===
        await SideAsync("Alerts, spinners & progress", "alerts, spinners & progress", "main .markdown-body h1");
        await Expect(Page.Locator(".guide-demo .sample-result-body .alert").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });
        await Expect(Page.Locator(".guide-demo .sample-result-body .spinner-border").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator(".guide-demo .sample-result-body .progress .progress-bar").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // === Navbar & nav — the BsNavItems render as SPA-routed anchors (data-rask-nav), the same primitive
        //     the showcase chrome is built from. ===
        await SideAsync("Navbar & nav", "navbar & nav", "main .markdown-body h1");
        await Expect(Page.Locator(".guide-demo .sample-result-body .navbar").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });
        Assert.True(
            await Page.Locator(".guide-demo .sample-result-body .nav .nav-link[data-rask-nav]").CountAsync() > 0);

        // === Modals, offcanvas & dropdowns ===
        await SideAsync("Modals, offcanvas & dropdowns", "modals, offcanvas & dropdowns", "main .markdown-body h1");

        // Modal — open + close driven by Rask state, no bootstrap.js loaded. The runtime focus trap
        // (data-rask-focus-trap in rask-dom.js) moves focus into the dialog on open, Escape dismisses
        // it via the backdrop-close handler, and focus returns to the trigger on close.
        var launchModal = Page.Locator(".guide-demo button:has-text(\"Launch demo modal\")").First;
        await Expect(launchModal).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });
        await launchModal.ClickAsync();
        var dialog = Page.Locator("div.modal.show").First;
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        // Focus was trapped into the dialog — the trap focuses the modal element itself (tabindex=-1).
        await Expect(dialog).ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 5_000 });
        // Escape closes it — no bootstrap.js, no btn-close click.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator("div.modal.show")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        // Focus returned to the trigger button.
        await Expect(launchModal).ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 5_000 });

        // Full-screen-below-sm modal — FullscreenBelow: Bp.Sm adds .modal-fullscreen-sm-down.
        await Page.Locator(".guide-demo button:has-text(\"Full-screen on phones\")").First.ClickAsync();
        await Expect(Page.Locator("div.modal.show .modal-dialog.modal-fullscreen-sm-down").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Page.Locator("div.modal .btn-close").First.ClickAsync();
        await Expect(Page.Locator("div.modal.show")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });

        // Dropdown (Popper-less, controlled) opened inside the same overflow:hidden card — the menu is
        // re-anchored position:fixed by the same helper so it isn't clipped. Selecting an item closes the
        // menu (its handler sets Open=false) and updates the readout. AlignEnd is covered by the unit test.
        await Page.Locator("#demo-dropdown .dropdown-toggle").First.ClickAsync();
        var ddMenu = Page.Locator("#demo-dropdown .dropdown-menu.show").First;
        await Expect(ddMenu).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // Retry the position read: on the slower WASM transport the data-rask-popover re-anchor can land a
        // beat after the menu paints, and this page is freshly navigated (not a long pre-hydrated one).
        await Expect(ddMenu).ToHaveCSSAsync("position", "fixed",
            new LocatorAssertionsToHaveCSSOptions { Timeout = 10_000 });
        await Expect(ddMenu).ToBeInViewportAsync(
            new LocatorAssertionsToBeInViewportOptions { Timeout = 10_000 });
        await ddMenu.Locator("button").Filter(new LocatorFilterOptions { HasText = "Archive" }).First.ClickAsync();
        await Expect(Page.Locator("#demo-dropdown-out")).ToContainTextAsync("Archive",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Offcanvas — the panel stays in the DOM and slides in via .show; the trigger opens it, a
        // backdrop click dismisses it, all through the live diff (no bootstrap.js).
        await Page.Locator(".guide-demo button:has-text('Open settings')").First.ClickAsync();
        var drawer = Page.Locator(".guide-demo .sample-result-body .offcanvas.show").First;
        await Expect(drawer).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        // Dispatch the click rather than actuate it: the .offcanvas-backdrop is position:fixed far down a
        // long guide page, where Playwright's actionability/scroll handling can fail to land a real click on
        // a fixed Bs overlay. DispatchEventAsync fires the handler directly — the same idiom the sidebar
        // drawer dismissal uses above.
        await Page.Locator(".guide-demo .sample-result-body .offcanvas-backdrop").First.DispatchEventAsync("click");
        await Expect(Page.Locator(".guide-demo .sample-result-body .offcanvas.show")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });

        // Confirm dialog — a BsModal-backed prompt. Confirming runs the action (updates the readout) and
        // closes it; the destructive confirm button is btn-danger.
        await Page.Locator(".guide-demo button:has-text('Delete item')").First.ClickAsync();
        var confirm = Page.Locator("div.modal.show").First;
        await Expect(confirm).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await confirm.Locator("button.btn-danger:has-text('Delete')").First.ClickAsync();
        await Expect(Page.Locator("div.modal.show")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(Page.Locator("#bs-confirm-status")).ToContainTextAsync("deleted",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // === Toasts — shown and dismissed entirely by live-diff state (no bootstrap.js, no data-bs-dismiss).
        //     Showing renders class="toast show"; the × removes it. ===
        await SideAsync("Toasts", "toasts", "main .markdown-body h1");
        var showToast = Page.Locator(".guide-demo button:has-text('Show toast')").First;
        await Expect(showToast).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });
        await showToast.ClickAsync();
        var toast = Page.Locator(".guide-demo .sample-result-body .toast.show").First;
        await Expect(toast).ToContainTextAsync("Hello, world!",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await toast.Locator(".btn-close").ClickAsync();
        await Expect(Page.Locator(".guide-demo .sample-result-body .toast.show")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        // === Cards, lists & tables — the static content components (breadcrumb, list group, placeholder
        //     skeletons) render in a real browser. These carry no interactive state, so a structural
        //     visibility assertion is the browser-side proof; their exact markup is unit-tested. ===
        await SideAsync("Cards, lists & tables", "cards, lists & tables", "main .markdown-body h1");
        await Expect(Page.Locator(".guide-demo .sample-result-body .breadcrumb").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });
        await Expect(Page.Locator(".guide-demo .sample-result-body .list-group .list-group-item").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator(".guide-demo .sample-result-body .placeholder").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // BsTable renders its rows (static — structural proof; exact markup is unit-tested).
        await Expect(Page.Locator(".guide-demo .sample-result-body table.table tbody tr").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // BsPagination — clicking a page button flips _page through the live runtime: the .active marker
        // moves to that page and the readout updates. Each item is a real <button>, no bootstrap.js.
        await Page.Locator(".guide-demo .sample-result-body .pagination .page-link")
            .Filter(new LocatorFilterOptions { HasText = "3" }).First.ClickAsync();
        await Expect(Page.Locator(".guide-demo .sample-result-body .pagination .page-item.active .page-link"))
            .ToHaveTextAsync("3", new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#bs-pagination-status")).ToContainTextAsync("Page 3",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // === Form controls — the IFormControl<T>-bound demo, incl. the in-form single-select (BsSelect). ===
        await SideAsync("Form controls", "form controls", "main .markdown-body h1");

        // Single-select (BsSelect) — a .form-select DISPLAY combobox <div>. Clicking opens the .dropdown-menu
        // listbox (re-anchored position:fixed by the overflow-escape helper). A Filter predicate adds a SEARCH
        // FIELD in the dropdown: typing there narrows the options; picking writes the bound model and the box
        // shows the option's label, then closes. Component markup is unit-tested in BsSelectTests.
        var plan = Page.Locator("#bs-plan");
        await Expect(plan).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });
        await plan.ClickAsync();
        var planMenu = Page.Locator("#bs-plan-list.dropdown-menu.show");
        await Expect(planMenu).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(planMenu).ToHaveCSSAsync("position", "fixed",
            new LocatorAssertionsToHaveCSSOptions { Timeout = 10_000 });
        // Type in the dropdown's search field — only the option whose label contains "Te" (Team) survives.
        await Page.Locator("#bs-plan-search").FillAsync("Te");
        await Expect(planMenu.Locator(".dropdown-item")).ToHaveCountAsync(1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await planMenu.Locator(".dropdown-item").First.ClickAsync();
        await Expect(plan).ToContainTextAsync("Team", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(planMenu).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });

        // Re-open to assert the combobox popover behaviours wired in rask-dom.js's installRaskPopover:
        await plan.ClickAsync();
        await Expect(planMenu).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // (1) WIDTH: the fixed menu is pinned to the trigger box, not stretched to the viewport. A w-100
        // menu carries .w-100 { width:100% !important }, which — once position:fixed — would resolve 100%
        // against the viewport unless the inline pin is written !important. Allow 2px for rounding.
        var planBox = await plan.BoundingBoxAsync();
        var planMenuBox = await planMenu.BoundingBoxAsync();
        Assert.True(planBox is not null && planMenuBox is not null
            && Math.Abs(planBox.Width - planMenuBox.Width) <= 2,
            $"open menu width {planMenuBox?.Width} should match the trigger {planBox?.Width}, not the viewport");
        // (2) FILTER FOCUS: opening a searchable select moves focus into its filter so the user types at once.
        await Expect(Page.Locator("#bs-plan-search"))
            .ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 10_000 });
        // (3) KEY CONTAINMENT: Enter in the filter picks the highlighted option and must NOT submit/validate
        // the surrounding <form> (whose other required fields would otherwise surface .invalid-feedback).
        await Page.Locator("#bs-plan-search").PressAsync("Enter");
        await Expect(planMenu).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });
        await Expect(Page.Locator("form:has(#bs-plan) .invalid-feedback"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        // Native fallback (Native: true) renders a real OS <select> (data-fed from the same Options), so
        // it degrades cleanly where the custom popover is unwanted (e.g. the native mobile host).
        var tier = Page.Locator("select#bs-tier");
        await Expect(tier).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(tier.Locator("option")).ToHaveCountAsync(3,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(tier.Locator("option").Filter(new LocatorFilterOptions { HasText = "Team" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        // Picking an option two-way-binds: the model updates (the readout echoes it) AND the <select> HOLDS
        // the new value — it must not snap back. (Regression: the selected <option> used to lose its
        // reconciliation key, desyncing the browser's live `selected` property so the box reverted.)
        await tier.SelectOptionAsync("team");
        await Expect(Page.Locator("#bs-readout")).ToContainTextAsync("Tier: Team",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(tier).ToHaveValueAsync("team", new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });

        // Nullable select (#bs-seats, int?) — a plain dropdown (no Filter → no search field). Pick a value;
        // the × clear then resets it to null so the box shows the "Any" placeholder again.
        var seats = Page.Locator("#bs-seats");
        await seats.ClickAsync();
        await Page.Locator("#bs-seats-list .dropdown-item").Filter(new LocatorFilterOptions { HasText = "2 seats" })
            .First.ClickAsync();
        await Expect(seats).ToContainTextAsync("2 seats", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator(".dropdown:has(#bs-seats) .bs-select-clear").First.ClickAsync();
        await Expect(seats).ToContainTextAsync("Any", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // === Selects & multiselect — the BsSelect/BsMultiSelect variant galleries. ===
        await SideAsync("Selects & multiselect", "selects & multiselect", "main .markdown-body h1");

        // BsSelect variant gallery — the native variant binds and holds its pick; a custom basic pick writes
        // the model. Both are echoed by the gallery's own readout.
        var selTier = Page.Locator("select#sel-tier");
        await Expect(selTier).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });
        await selTier.SelectOptionAsync("team");
        await Expect(selTier).ToHaveValueAsync("team", new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });
        var selPlan = Page.Locator("#sel-plan");
        await selPlan.ClickAsync();
        await Page.Locator(".dropdown:has(#sel-plan) .dropdown-menu.show .dropdown-item").First.ClickAsync();
        await Expect(Page.Locator("#sel-readout")).ToContainTextAsync("Plan: Free",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#sel-readout")).ToContainTextAsync("Tier: Team",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Stacking regression: the clear × must not paint over ANOTHER select's open dropdown menu. Both a ×
        // and an open .dropdown-menu.show default to z-index 1000, so with the × always raised the later-in-DOM
        // × won the tie and showed through. Fix: the × gets its raised z-index (needed to clear its OWN
        // click-outside backdrop at 999) ONLY while its own control is open, via .bs-clear-open — so a *closed*
        // select's × drops to z-index:auto and sits behind any open menu. Assert the computed z-index in both
        // states on the clearable Seats select (giving it a value surfaces the ×).
        var galSeats = Page.Locator("#sel-seats");
        await galSeats.ScrollIntoViewIfNeededAsync();
        await galSeats.ClickAsync();
        await Page.Locator(".dropdown:has(#sel-seats) .dropdown-menu.show .dropdown-item")
            .Filter(new LocatorFilterOptions { HasText = "2 seats" }).First.ClickAsync();
        var galSeatsClear = Page.Locator(".dropdown:has(#sel-seats) .bs-select-clear");
        await Expect(galSeatsClear).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // Closed: no raised-z-index hook, so the × computes to z-index:auto (paints behind any open menu).
        Assert.DoesNotContain("bs-clear-open", await galSeatsClear.GetAttributeAsync("class") ?? "");
        Assert.Equal("auto", await galSeatsClear.EvaluateAsync<string>("x => getComputedStyle(x).zIndex"));
        // Re-open this select: its OWN × must rise to z-index 1000 to stay clickable above its backdrop (999).
        await galSeats.ClickAsync();
        await Expect(Page.Locator(".dropdown:has(#sel-seats) .dropdown-menu.show"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        Assert.Contains("bs-clear-open", await galSeatsClear.GetAttributeAsync("class") ?? "");
        Assert.Equal("1000", await galSeatsClear.EvaluateAsync<string>("x => getComputedStyle(x).zIndex"));
        // Close via the backdrop so it doesn't intercept the next click.
        await Page.Locator(".dropdown:has(#sel-seats) .position-fixed").DispatchEventAsync("click");
        await Expect(Page.Locator(".dropdown:has(#sel-seats) .dropdown-menu.show"))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });

        // BsMultiSelect variant gallery — ticking two options in the basic control shows them as chips and
        // the gallery readout lists them.
        var msBasic = Page.Locator("#ms-basic");
        await msBasic.ClickAsync();
        var msMenu = Page.Locator("#ms-basic .dropdown-menu.show");
        await Expect(msMenu).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await msMenu.Locator(".dropdown-item").Nth(0).ClickAsync();
        await msMenu.Locator(".dropdown-item").Nth(1).ClickAsync();
        await Expect(Page.Locator("#ms-readout")).ToContainTextAsync("Web, Mobile",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        // A multiselect stays open while you tick items — close it so its full-viewport backdrop doesn't
        // intercept the next navigation click.
        await Page.Locator("#ms-basic .position-fixed").DispatchEventAsync("click");
        await Expect(msMenu).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });

        // Grouped single-select — OptionGroup renders .dropdown-header sections and OptionDisabled greys a
        // non-selectable option (the "Data" team); picking an enabled option writes the bound id.
        var grouped = Page.Locator("#sel-grouped");
        await grouped.ClickAsync();
        var groupedMenu = Page.Locator(".dropdown:has(#sel-grouped) .dropdown-menu.show");
        await Expect(groupedMenu).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(groupedMenu.Locator(".dropdown-header").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(groupedMenu.Locator(".dropdown-item[disabled]"))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await groupedMenu.Locator(".dropdown-item:not([disabled])").First.ClickAsync();
        await Expect(Page.Locator("#sel-readout")).ToContainTextAsync("GroupedTeam: Platform",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Grouped multiselect — OptionGroup renders three .dropdown-header sections, SelectAll adds a bulk
        // "Select all" header, and the disabled "Games" is a non-interactive option. (The select-all/disabled
        // toggle behaviour is unit-tested; here we verify the grouped structure renders in a real browser —
        // count-based checks avoid depending on the fixed popover scrolling each row into the viewport.)
        var msGrouped = Page.Locator("#ms-grouped");
        await msGrouped.ClickAsync();
        var msGroupedMenu = Page.Locator("#ms-grouped .dropdown-menu.show");
        await Expect(msGroupedMenu).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(msGroupedMenu.Locator(".dropdown-header"))
            .ToHaveCountAsync(3, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 }); // Frontend/Data/Other
        await Expect(msGroupedMenu.Locator(".dropdown-item").Filter(new LocatorFilterOptions { HasText = "Select all" }))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(msGroupedMenu.Locator(".dropdown-item[disabled]"))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 }); // "Games" disabled
        await Page.Locator("#ms-grouped .position-fixed").DispatchEventAsync("click");
        await Expect(msGroupedMenu).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });

        // === Date & time pickers — hand-editable, custom-popover calendar/clock controls (no bootstrap.js). ===
        await SideAsync("Date & time pickers", "date & time pickers", "main .markdown-body h1");
        await Expect(Page.Locator("#pick-date").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        // Date picker (custom popover) — the calendar opens, is navigated and a day picked entirely from
        // Rask live-diff state (no bootstrap.js). Picking the 1st of the current month writes the bound
        // model; the readout rendered OUTSIDE the Form updates, proving the two-way bind round-trips with
        // no StateHasChanged. Cell ids are invariant (yyyyMMdd), so this is deterministic on any date.
        var ym = DateTime.Today.ToString("yyyyMM");
        var firstIso = DateTime.Today.ToString("yyyy-MM") + "-01";
        await Page.Locator("#pick-date").First.ClickAsync();
        await Expect(Page.Locator("#pick-date-cal.bs-cal[role=\"grid\"]").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // The popover escapes the .sample-card { overflow:hidden } wrapper via the data-rask-popover helper
        // (rask-dom.js). That position:fixed re-anchor is asserted directly — on WASM too — for the dropdown
        // (overlays page) and the in-form select (form-controls page) above; here we exercise it functionally
        // instead: the day cell being clickable proves the calendar isn't clipped. (On a freshly-navigated
        // WASM pickers page the picker re-renders per keystroke, so a late live-diff frame can transiently
        // reset the open menu's inline position — re-asserting the CSS property here is racy, and redundant.)
        await Page.Locator($"#pick-date-d-{ym}01").First.ClickAsync();
        await Expect(Page.Locator("#pick-readout")).ToContainTextAsync(firstIso,
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Hand-editable: the picker box is a text <input>. Typing a date commits live per keystroke (culture
        // parse; ISO yyyy-MM-dd is accepted in any culture), so the bound readout updates with no calendar.
        var dateInput = Page.Locator("#pick-date").First;
        await dateInput.FillAsync("2026-12-25");
        await Expect(Page.Locator("#pick-readout")).ToContainTextAsync("2026-12-25",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        // An unparseable entry keeps the raw text (no mid-typing revert) and leaves the value unchanged.
        await dateInput.FillAsync("not a date");
        await Expect(dateInput).ToHaveValueAsync("not a date",
            new LocatorAssertionsToHaveValueOptions { Timeout = 5_000 });
        await Expect(Page.Locator("#pick-readout")).ToContainTextAsync("2026-12-25",
            new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
        // Close the opened picker so its full-viewport backdrop doesn't intercept later clicks. (The nullable
        // picker's × clear is unit-tested; the ×-clears-to-null click is E2E-covered on #bs-seats above.)
        await Page.Locator(".dropdown:has(#pick-date) .position-fixed").DispatchEventAsync("click");

        // Date-time picker (#pick-datetime, bound to a DateTime): open the calendar and pick a day. The
        // composed value must write back as a DateTime — regression guard for the "Object of type
        // DateTimeOffset cannot be converted to type DateTime" write crash (BoxValue could box the wrong
        // date/time type; WriteBoxedAsync now coerces to the property's real type). A crash surfaces as the
        // Rask error boundary; the readout's third segment updating proves the write succeeded.
        await Page.Locator("#pick-datetime").First.ClickAsync();
        await Expect(Page.Locator("#pick-datetime-cal.bs-cal[role=\"grid\"]").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Page.Locator($"#pick-datetime-d-{ym}15").First.ClickAsync();
        await Expect(Page.Locator("#pick-readout"))
            .ToContainTextAsync(DateTime.Today.ToString("yyyy-MM") + "-15",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        // Also pick an hour + minute (the time columns compose with the date — same DateTime write path),
        // and type a full value into the box (the parse path). None may throw the DateTimeOffset cast.
        var dtMenu = Page.Locator(".dropdown-menu.show:has(#pick-datetime-cal)").First;
        await dtMenu.Locator(".bs-time-col").Nth(0)
            .GetByText("11", new LocatorGetByTextOptions { Exact = true }).First.ClickAsync();
        await dtMenu.Locator(".bs-time-col").Nth(1)
            .GetByText("30", new LocatorGetByTextOptions { Exact = true }).First.ClickAsync();
        await Expect(Page.Locator("#pick-readout")).ToContainTextAsync("-15 11:30",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator("#pick-datetime").First.FillAsync("2026-12-25 14:45");
        await Expect(Page.Locator("#pick-readout")).ToContainTextAsync("2026-12-25 14:45",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator(".dropdown:has(#pick-datetime) .position-fixed").DispatchEventAsync("click");

        // Keyboard navigation (a11y): the calendar is fully operable from the keyboard, which only a browser
        // proves end-to-end. Focus opens the popover; ArrowRight moves a virtual cursor (aria-activedescendant
        // on the box, not DOM focus); Enter selects it. Deterministic on any date — from the 1st, one step
        // right lands on the 2nd of the same month (every month has a 2nd).
        var secondIso = DateTime.Today.ToString("yyyy-MM") + "-02";
        var kbDate = Page.Locator("#pick-date").First;
        await kbDate.FillAsync(firstIso);
        await Expect(Page.Locator("#pick-readout")).ToContainTextAsync(firstIso,
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await kbDate.FocusAsync();
        await kbDate.PressAsync("ArrowRight");
        await Expect(kbDate).ToHaveAttributeAsync("aria-activedescendant", $"pick-date-d-{ym}02",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 10_000 });
        await kbDate.PressAsync("Enter");
        await Expect(Page.Locator("#pick-readout")).ToContainTextAsync(secondIso,
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Time picker keyboard: focus opens the clock; ArrowDown nudges the minute by the step (15). From
        // 09:00 the readout's time segment moves to 09:15 — the nudge writes the bound TimeOnly, no click.
        var kbTime = Page.Locator("#pick-time").First;
        await kbTime.FillAsync("09:00");
        await Expect(Page.Locator("#pick-readout")).ToContainTextAsync("09:00",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await kbTime.FocusAsync();
        await kbTime.PressAsync("ArrowDown");
        await Expect(Page.Locator("#pick-readout")).ToContainTextAsync("09:15",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        // Home/End jump to the day edge (this picker has no Min/Max): End → 23:59, Home → 00:00.
        await kbTime.PressAsync("End");
        await Expect(Page.Locator("#pick-readout")).ToContainTextAsync("23:59",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await kbTime.PressAsync("Home");
        await Expect(Page.Locator("#pick-readout")).ToContainTextAsync("00:00",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator(".dropdown:has(#pick-time) .position-fixed").DispatchEventAsync("click");

        // Nullable picker × clears to null: set a deadline, then click the × — the readout's last segment
        // returns to the "—" placeholder, proving the clear writes null through the bound accessor.
        var kbDeadline = Page.Locator("#pick-deadline").First;
        await kbDeadline.FillAsync("2026-11-20");
        await Expect(Page.Locator("#pick-readout")).ToContainTextAsync("2026-11-20",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator(".dropdown:has(#pick-deadline) .bs-picker-clear").First.ClickAsync();
        await Expect(Page.Locator("#pick-readout")).ToContainTextAsync("—",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Page.Locator(".dropdown:has(#pick-deadline) .position-fixed").DispatchEventAsync("click");
    }

    // The data-grid guide (docs/data-grid.md). BsDataGrid is the showcase's most stateful component, and its
    // sort/page/expand transitions are unit-tested in BsDataGridInteractionTests; what only a browser proves is
    // that those transitions survive the real live morph over each host's transport (Server WS / WASM
    // JSImport) — a re-ordered <tbody> and a keyed detail-row insert are exactly where a diff bug would hide.
    protected async Task WalkDataGridGuideAsync()
    {
        // The heading match is case-sensitive; this page's h1 is just "Data grid" (the Bootstrap group pages
        // read "Bootstrap — buttons & badges", which is why those walks assert lowercase).
        await SideAsync("Data grid", "Data grid", "main .markdown-body h1");

        // === Sorting — clicking a header re-orders the rows in the real DOM and flips aria-sort. ===
        var demo = Page.Locator("#grid-demo");
        var grid = Page.Locator("#bs-grid");
        await Expect(grid).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });
        await Expect(grid.Locator("th[aria-sort='ascending']")).ToHaveCountAsync(0);

        var before = await grid.Locator("tbody tr td:nth-child(1)").AllInnerTextsAsync();
        var productHeader = grid.Locator("th:has-text('Product')").First;
        await productHeader.Locator("button").ClickAsync();
        await Expect(productHeader).ToHaveAttributeAsync("aria-sort", "ascending",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });

        var ascending = await grid.Locator("tbody tr td:nth-child(1)").AllInnerTextsAsync();
        Assert.Equal(ascending.OrderBy(x => x, StringComparer.Ordinal), ascending);
        Assert.NotEqual(before, ascending);

        // A second click flips to descending. The grid is paged (5 of 12), so page 1 descending is the tail of
        // the list reversed — NOT the reverse of page 1 ascending. Assert the order it actually claims.
        await productHeader.Locator("button").ClickAsync();
        await Expect(productHeader).ToHaveAttributeAsync("aria-sort", "descending",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });
        var descending = await grid.Locator("tbody tr td:nth-child(1)").AllInnerTextsAsync();
        Assert.Equal(descending.OrderByDescending(x => x, StringComparer.Ordinal), descending);
        Assert.NotEqual(ascending, descending);

        // Only one column may claim a direction at a time.
        await grid.Locator("th:has-text('Category') button").First.ClickAsync();
        await Expect(grid.Locator("th[aria-sort='ascending']")).ToHaveCountAsync(1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });

        // === Paging — page 2 shows a DISJOINT slice, and the footer total spans the whole set, not the page.
        //     A footer computed over the visible page would change here; it must not. ===
        var footer = await grid.Locator("tfoot td").Last.InnerTextAsync();
        var page1 = await grid.Locator("tbody tr td:nth-child(1)").AllInnerTextsAsync();
        await demo.Locator(".pagination li button:has-text('2')").First.ClickAsync();
        await Expect(demo.Locator(".pagination li:has(button:text-is('2'))")).ToHaveClassAsync(
            new Regex("active"), new LocatorAssertionsToHaveClassOptions { Timeout = 15_000 });

        var page2 = await grid.Locator("tbody tr td:nth-child(1)").AllInnerTextsAsync();
        Assert.Empty(page2.Intersect(page1));
        Assert.Equal(footer, await grid.Locator("tfoot td").Last.InnerTextAsync());

        // The active page is the app's brand primary, not Bootstrap's blue. Bootstrap derives every OTHER
        // pagination colour from a CSS variable but bakes the literal hex #0d6efd into --bs-pagination-
        // active-bg, so an app that themes --bs-primary used to get a purple surface with a blue active page.
        // rask-bootstrap.css re-points it at the runtime var; only a real browser resolves that cascade, so
        // the assertion lives here. The showcase's --bs-primary is #7C3AED.
        // ToHaveCSS, not a one-shot getComputedStyle: .page-link transitions background-color over .15s, so
        // reading it the instant the class lands samples the fade (a near-white part-way colour that differs
        // run to run). This retries until it settles.
        await Expect(demo.Locator(".pagination li.active .page-link").First)
            .ToHaveCSSAsync("background-color", "rgb(124, 58, 237)",
                new LocatorAssertionsToHaveCSSOptions { Timeout = 15_000 });

        // Sorting returns to page 1 — otherwise the user lands mid-way through a list they just re-ordered.
        await grid.Locator("th:has-text('Product') button").First.ClickAsync();
        await Expect(demo.Locator(".pagination li:has(button:text-is('1'))")).ToHaveClassAsync(
            new Regex("active"), new LocatorAssertionsToHaveClassOptions { Timeout = 15_000 });

        // === Master-detail — the expander inserts a keyed detail <tr>; two can be open at once, and closing
        //     one leaves the other untouched (the keyed-insert claim the live diff makes). ===
        var detail = Page.Locator("#bs-grid-detail");
        await Expect(detail).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        // The expanded detail renders a nested BsTable, so a bare "tbody tr" also counts its line rows. Only the
        // grid's own rows are direct children of its tbody.
        var outerRows = detail.Locator("> tbody > tr");
        var collapsedRows = await outerRows.CountAsync();

        var first = detail.Locator("> tbody > tr button[aria-expanded]").First;
        await Expect(first).ToHaveAttributeAsync("aria-expanded", "false");
        await first.ClickAsync();
        await Expect(first).ToHaveAttributeAsync("aria-expanded", "true",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });
        await Expect(outerRows).ToHaveCountAsync(collapsedRows + 1);
        await Expect(detail.Locator("> tbody > tr > td[colspan]").First).ToBeVisibleAsync();

        var second = detail.Locator("> tbody > tr button[aria-expanded]").Nth(1);
        await second.ClickAsync();
        await Expect(detail.Locator("> tbody > tr > td[colspan]")).ToHaveCountAsync(2,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(first).ToHaveAttributeAsync("aria-expanded", "true");

        await first.ClickAsync();
        await Expect(detail.Locator("> tbody > tr > td[colspan]")).ToHaveCountAsync(1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });

        // === Empty state — the placeholder replaces the whole table, and the grid comes back. ===
        // The grid's Id lands on the <table> itself, so #bs-grid-empty IS the table: it disappears entirely
        // when Empty takes over, rather than emptying out.
        var emptyGrid = Page.Locator("#bs-grid-empty");
        await Expect(emptyGrid).ToHaveCountAsync(1);
        await Page.Locator("#grid-filter-none").ClickAsync();
        await Expect(Page.Locator("#grid-empty")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Expect(emptyGrid).ToHaveCountAsync(0);

        await Page.Locator("#grid-filter-clear").ClickAsync();
        await Expect(emptyGrid).ToHaveCountAsync(1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });

        await WalkDataGridGroupAsync();
        await WalkDataGridColumnsAsync();
        await WalkDataGridSelectionAsync();
        await WalkDataGridRowsAsync();
        await WalkDataGridLoadingAsync();
        await WalkDataGridStickyAsync();
    }

    // The column chooser, driven from the keyboard-first menu. The unit tests pin the fold/reorder arithmetic;
    // the browser proves the real live morph — hiding rewrites the header AND every row's cells, reordering
    // permutes them, and sort still resolves to the right column afterwards through the whole transport.
    private async Task WalkDataGridColumnsAsync()
    {
        var demo = Page.Locator("#grid-columns-demo");
        var grid = Page.Locator("#bs-grid-columns");
        await Expect(grid).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        var headers = grid.Locator("thead th");
        await Expect(headers).ToHaveTextAsync(["Account", "Region", "Rep", "Amount"]);

        // Open the menu — a real disclosure button, no drag needed.
        await demo.Locator("button[aria-label='Columns']").ClickAsync();
        await Expect(demo.Locator(".bs-grid-columnmenu")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // === Hide — unchecking Region folds its header (and cells) out of the table. ===
        await demo.Locator("input[aria-label='Show Region']").ClickAsync();
        await Expect(grid.Locator("thead th:has-text('Region')")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(headers).ToHaveCountAsync(3);

        // Re-show it — the menu row outlives the hidden column, which is the only way back.
        await demo.Locator("input[aria-label='Show Region']").ClickAsync();
        await Expect(grid.Locator("thead th:has-text('Region')")).ToHaveCountAsync(1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });

        // === Reorder — moving Amount earlier permutes the header order in the real DOM (Amount before Rep). ===
        await demo.Locator("button[aria-label='Move Amount earlier']").ClickAsync();
        await Expect(headers).ToHaveTextAsync(["Account", "Region", "Amount", "Rep"],
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });

        // === Sort survives the reorder — clicking the moved Amount header still sorts by Amount, not by
        //     whatever column now sits at that slot. ===
        await grid.Locator("th:has-text('Amount') button").ClickAsync();
        await Expect(grid.Locator("th:has-text('Amount')")).ToHaveAttributeAsync("aria-sort", "ascending",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });
        await Expect(grid.Locator("th[aria-sort='ascending']")).ToHaveCountAsync(1);
    }

    // Grouping. The unit tests pin the banding; the browser proves the ordering guarantee survives the real
    // live morph — re-banding and re-sorting rewrite the <tbody> wholesale, which is exactly where a diff bug
    // would hide.
    private async Task WalkDataGridGroupAsync()
    {
        var demo = Page.Locator("#grid-group-demo");
        var grid = Page.Locator("#bs-grid-group");
        await Expect(grid).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        // === One level. The demo's source list is NOT ordered by region, so each region appearing exactly
        //     once is the ordering guarantee doing its job. ===
        var bands = grid.Locator("tbody tr.table-group-divider");
        await Expect(bands).ToHaveCountAsync(3); // AMER, APAC, EMEA
        await Expect(bands.First).ToContainTextAsync("Region: AMER");

        // === Grouped columns fold away (default). Region is grouped, so its own <th> is gone — the value lives
        //     only in the band header. "Show grouped column" flips ShowGroupedColumns to bring the column back;
        //     toggle it off again so the rest of the walk runs against the default. ===
        await Expect(grid.Locator("thead th:has-text('Region')")).ToHaveCountAsync(0);
        await demo.Locator("#group-show-col").ClickAsync();
        await Expect(grid.Locator("thead th:has-text('Region')")).ToHaveCountAsync(1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await demo.Locator("#group-show-col").ClickAsync();
        await Expect(grid.Locator("thead th:has-text('Region')")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });

        // === The user's sort applies WITHIN a band, never across it. Sorting by Account keeps three bands. ===
        await grid.Locator("th:has-text('Account') button").ClickAsync();
        await Expect(grid.Locator("th:has-text('Account')")).ToHaveAttributeAsync("aria-sort", "ascending",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });
        await Expect(bands).ToHaveCountAsync(3);

        var emeaAccounts = await grid.Locator("tbody tr:not(.table-group-divider):not(.table-light) td:nth-child(1)")
            .AllInnerTextsAsync();
        Assert.NotEmpty(emeaAccounts);

        // === Subtotals — one per band, plus the grand total in <tfoot>. ===
        await Expect(grid.Locator("tbody tr.table-light")).ToHaveCountAsync(3);
        await Expect(grid.Locator("tfoot")).ToBeVisibleAsync();

        // === Collapse — keyed by the band's value, so it survives the re-render. ===
        var firstToggle = grid.Locator("tbody tr.table-group-divider button[aria-expanded]").First;
        await Expect(firstToggle).ToHaveAttributeAsync("aria-expanded", "true");
        var rowsBefore = await grid.Locator("tbody tr:not(.table-group-divider):not(.table-light)").CountAsync();
        await firstToggle.ClickAsync();
        await Expect(firstToggle).ToHaveAttributeAsync("aria-expanded", "false",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });
        await Expect(grid.Locator("tbody tr:not(.table-group-divider):not(.table-light)"))
            .Not.ToHaveCountAsync(rowsBefore);
        await Expect(bands).ToHaveCountAsync(3); // the band header stays; only its rows go

        await firstToggle.ClickAsync();
        await Expect(firstToggle).ToHaveAttributeAsync("aria-expanded", "true",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });

        // === Nesting — region ▸ rep: more bands, and the outer ones still appear once each. ===
        // 3 region bands + 5 region/rep bands (EMEA has Ana and Dee, AMER has Bo and Ana, APAC only Cy).
        await demo.Locator("#group-nested").ClickAsync();
        await Expect(bands).ToHaveCountAsync(8,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(grid.Locator("tbody tr.table-group-divider:has-text('Region:')")).ToHaveCountAsync(3);

        // === Ungrouped — every band goes, the rows stay. ===
        await demo.Locator("#group-none").ClickAsync();
        await Expect(bands).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(grid.Locator("tbody tr")).ToHaveCountAsync(9);
        await Expect(grid.Locator("tbody tr.table-light")).ToHaveCountAsync(0);

        await demo.Locator("#group-region").ClickAsync();
        await Expect(bands).ToHaveCountAsync(3,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });

        await WalkDataGridGroupPanelAsync(demo, grid, bands);
    }

    // The group panel, driven BOTH ways. The keyboard walk is the point: the panel's promise is that drag is an
    // accelerator, not the only way in, and only a browser can prove a real Enter keypress on a real focused
    // button does the same thing a drag does.
    private async Task WalkDataGridGroupPanelAsync(ILocator demo, ILocator grid, ILocator bands)
    {
        var panel = demo.Locator(".bs-grid-grouppanel");
        await Expect(panel).ToBeVisibleAsync();
        await Expect(panel.Locator(".bs-grid-chip")).ToHaveCountAsync(1); // region

        // === KEYBOARD ONLY. Press Enter on the Rep header's group control — no pointer at all. ===
        // The settle is load-bearing, and cost an hour to find. The steps above end with a click that
        // re-groups the whole grid, and its LAST live frame can land a beat after the assertion that waited
        // for it: the diff then replaces this very button, and a keypress aimed at it lands on a detached
        // node — silently, because a key that hits nothing raises nothing. Let the re-render finish first.
        // (Locator.Press does not help: its auto-wait re-resolves the element, but cannot know a frame is
        // still in flight.)
        await Page.WaitForTimeoutAsync(500);
        await grid.Locator("th:has-text('Rep') button[aria-label='Group by Rep']").PressAsync("Enter");
        await Expect(panel.Locator(".bs-grid-chip")).ToHaveCountAsync(2,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        // region ▸ rep, the same nesting the buttons produced above. Grouping is two renders — the grid's own
        // (from the click) and the consumer's (from OnGroupedChange) — so give it the same room as its
        // neighbours rather than the 5s default.
        await Expect(bands).ToHaveCountAsync(8,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });

        // Renest from the keyboard: move Rep out one level, so rep ▸ region.
        await panel.Locator("button[aria-label='Move Rep out one level']").PressAsync("Enter");
        await Expect(panel.Locator(".bs-grid-chip").First).ToContainTextAsync("Rep",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        // The ends are really disabled, not merely styled that way.
        await Expect(panel.Locator("button[aria-label='Move Rep out one level']")).ToBeDisabledAsync();

        // Ungroup from the keyboard.
        await panel.Locator("button[aria-label='Stop grouping by Rep']").PressAsync("Enter");
        await Expect(panel.Locator(".bs-grid-chip")).ToHaveCountAsync(1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });

        // === DRAG is wired, but is not asserted end-to-end here, and that is deliberate. Rask uses NATIVE
        //     HTML5 drag-and-drop (dragstart/dragover/drop), and Playwright's DragTo synthesises mouse
        //     move/down/up — which the browser does not turn into native drag events. Driving it needs manual
        //     dispatchEvent with a hand-built DataTransfer, which tests the harness more than the feature.
        //     The keyboard path above IS the accessible path and is proven end-to-end; the drag is the
        //     pointer accelerator over the same handlers. Assert its wiring is present rather than fake a
        //     gesture the harness can't faithfully produce. ===
        var repHeader = grid.Locator("th:has-text('Rep')");
        await Expect(repHeader).ToHaveAttributeAsync("draggable", "true");
        await Expect(repHeader).ToHaveAttributeAsync("data-rask-on-dragstart", new Regex(".+"));
        await Expect(panel).ToHaveAttributeAsync("data-rask-on-drop", new Regex(".+"));
        await Expect(panel.Locator(".bs-grid-chip").First).ToHaveAttributeAsync("draggable", "true");
    }

    // Selection driving a bulk action. The unit tests pin the set arithmetic; the browser proves the part that
    // only a real transport shows — that ticking a checkbox re-renders the grid's OWNER (the toolbar count
    // lives outside the grid), which is exactly what the consumer-resolution fix underneath this is about.
    private async Task WalkDataGridSelectionAsync()
    {
        var grid = Page.Locator("#bs-grid-selection");
        var archive = Page.Locator("#grid-bulk-archive");
        await Expect(grid).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        // Nothing selected: the bulk action is disabled and says so.
        await Expect(archive).ToHaveTextAsync("Archive 0 selected");
        await Expect(archive).ToBeDisabledAsync();

        var boxes = grid.Locator("tbody .form-check-input");
        await Expect(boxes).ToHaveCountAsync(6);

        // Tick two rows — the count outside the grid tracks, and the rows mark themselves.
        await boxes.Nth(0).CheckAsync();
        await Expect(archive).ToHaveTextAsync("Archive 1 selected",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await boxes.Nth(2).CheckAsync();
        await Expect(archive).ToHaveTextAsync("Archive 2 selected",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await Expect(grid.Locator("tbody tr.table-active")).ToHaveCountAsync(2);
        await Expect(archive).ToBeEnabledAsync();

        // Selection is keyed, so it follows the rows through a sort rather than staying at those positions.
        var pickedBefore = await grid.Locator("tbody tr.table-active td:nth-child(2)").AllInnerTextsAsync();
        await grid.Locator("th:has-text('Task') button").ClickAsync();
        await Expect(grid.Locator("th[aria-sort='ascending']")).ToHaveCountAsync(1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });

        var pickedAfter = await grid.Locator("tbody tr.table-active td:nth-child(2)").AllInnerTextsAsync();
        Assert.Equal(pickedBefore.OrderBy(x => x, StringComparer.Ordinal),
            pickedAfter.OrderBy(x => x, StringComparer.Ordinal));

        // Select-all covers the page, and its accessible name says exactly that.
        var all = grid.Locator("thead .form-check-input");
        await Expect(all).ToHaveAttributeAsync("aria-label", "Select all rows on this page");
        await all.CheckAsync();
        await Expect(archive).ToHaveTextAsync("Archive 6 selected",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });

        await all.UncheckAsync();
        await Expect(archive).ToHaveTextAsync("Archive 0 selected",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });

        // The bulk action consumes the reported keys.
        await boxes.Nth(0).CheckAsync();
        await Expect(archive).ToHaveTextAsync("Archive 1 selected",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await archive.ClickAsync();
        await Expect(grid.Locator("tbody tr")).ToHaveCountAsync(5,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(Page.Locator("#grid-bulk-done")).ToHaveTextAsync("Archived 1.");
    }

    // The busy state over a real transport. The unit tests pin the markup of each state; what only a browser
    // shows is the round trip — spinner appears, controls go inert, rows are replaced, spinner goes.
    private async Task WalkDataGridLoadingAsync()
    {
        var grid = Page.Locator("#bs-grid-loading");
        var demo = Page.Locator("#grid-loading-demo");
        await Expect(grid).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        // Idle: in use (Loading: false) so the wrapper is there, but no overlay and no aria-busy.
        await Expect(demo.Locator(".position-relative")).ToHaveCountAsync(1);
        await Expect(demo.Locator(".bs-grid-overlay")).ToHaveCountAsync(0);
        await Expect(grid).Not.ToHaveAttributeAsync("aria-busy", "true");

        var firstBefore = await grid.Locator("tbody tr td:nth-child(1)").First.InnerTextAsync();

        // Sorting awaits a 600ms fetch, so the overlay is observable. The demo's Loading flips true, the grid
        // re-renders, and only then does the await complete.
        await grid.Locator("th:has-text('City') button").ClickAsync();

        await Expect(demo.Locator(".bs-grid-overlay")).ToHaveCountAsync(1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(grid).ToHaveAttributeAsync("aria-busy", "true");
        // aria-disabled, not disabled: the control must stay focusable while it says it is inert.
        await Expect(grid.Locator("th:has-text('City') button")).ToHaveAttributeAsync("aria-disabled", "true");
        await Expect(demo.Locator(".pagination .page-item:not(.disabled)")).ToHaveCountAsync(0);

        // ...and it clears, leaving the rows sorted.
        await Expect(demo.Locator(".bs-grid-overlay")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(grid).Not.ToHaveAttributeAsync("aria-busy", "true");

        var sorted = await grid.Locator("tbody tr td:nth-child(1)").AllInnerTextsAsync();
        Assert.Equal(sorted.OrderBy(x => x, StringComparer.Ordinal), sorted);
        Assert.NotEqual(firstBefore, sorted[0]);

        // The wrapper survived the whole flip — it must never be torn down, or the table would lose its DOM
        // identity (and any focus or scroll inside it) on every fetch.
        await Expect(demo.Locator(".position-relative")).ToHaveCountAsync(1);

        // Paging awaits too, and lands on a disjoint slice. Wait out the whole fetch — overlay in, overlay
        // out — before reading the rows. Neither shortcut works here, and both fail in opposite directions:
        // "overlay is gone" is already true in the instant before the fetch starts, and "the pager says 2" is
        // true from the MID-AWAIT render onwards, while the rows are still the previous page's. The overlay's
        // two edges are the only signal that brackets the fetch itself.
        var pageOne = await grid.Locator("tbody tr td:nth-child(1)").AllInnerTextsAsync();
        await demo.Locator(".pagination li:has(button:text-is('2')) button").ClickAsync();
        await Expect(demo.Locator(".bs-grid-overlay")).ToHaveCountAsync(1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(demo.Locator(".bs-grid-overlay")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });

        var pageTwo = await grid.Locator("tbody tr td:nth-child(1)").AllInnerTextsAsync();
        Assert.Empty(pageOne.Intersect(pageTwo));
    }

    // Row clicks and conditional row styling. The unit tests prove which cells carry the handler; only a real
    // browser proves the consequence of that choice — that a click on the row body opens the row, while the
    // button inside a template cell still fires its OWN handler rather than being cancelled by an ancestor.
    private async Task WalkDataGridRowsAsync()
    {
        var grid = Page.Locator("#bs-grid-row");
        await Expect(grid).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        // === RowClass — the overdue invoices are tinted from their own data, the current ones are not. ===
        await Expect(grid.Locator("tbody tr.table-warning")).ToHaveCountAsync(1);
        await Expect(grid.Locator("tbody tr.table-danger")).ToHaveCountAsync(1);

        // === Row click — clicking a Value cell reports its row. ===
        await Expect(Page.Locator("#grid-row-opened")).ToHaveCountAsync(0);
        await grid.Locator("tbody tr:nth-child(2) td.bs-grid-click").First.ClickAsync();
        await Expect(Page.Locator("#grid-row-opened")).ToHaveTextAsync("Opened INV-1042",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });

        // === The Open button still works. This is the regression that matters: the button lives in a Template
        //     cell, which is NOT row-clickable, so no ancestor handler cancels its click. Were the row click
        //     attached to the <tr> instead, this button would be dead and nothing would say so. ===
        await grid.Locator("#open-INV-1044").ClickAsync();
        await Expect(Page.Locator("#grid-row-opened")).ToHaveTextAsync("Opened INV-1044",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });

        // The button's cell never carries a handler of its own.
        await Expect(grid.Locator("tbody tr:first-child td:last-child")).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("bs-grid-click"));
    }

    // The sticky header, which is only observable in a real layout: the assertion is that the header stays put
    // in the viewport while the rows scroll under it inside the bounded container.
    private async Task WalkDataGridStickyAsync()
    {
        var grid = Page.Locator("#bs-grid-sticky");
        await Expect(grid).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });
        await Expect(grid).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("bs-table-sticky"));

        var header = grid.Locator("thead th").First;
        var box = grid.Locator("xpath=..");  // the .table-responsive wrapper MaxHeight bounds

        var headerBefore = await header.BoundingBoxAsync();
        var firstRowBefore = await grid.Locator("tbody tr").First.BoundingBoxAsync();

        // Scroll the container, not the page: the header sticks to its nearest scroll container, which is
        // exactly what MaxHeight created.
        await box.EvaluateAsync("el => el.scrollTop = 200");
        await Page.WaitForTimeoutAsync(250);

        var headerAfter = await header.BoundingBoxAsync();
        var firstRowAfter = await grid.Locator("tbody tr").First.BoundingBoxAsync();

        Assert.NotNull(headerBefore);
        Assert.NotNull(headerAfter);
        Assert.NotNull(firstRowBefore);
        Assert.NotNull(firstRowAfter);

        // The rows moved up; the header did not move at all. Without position:sticky both would have moved.
        Assert.True(firstRowAfter!.Y < firstRowBefore!.Y - 100,
            $"rows should have scrolled up, but went {firstRowBefore.Y} -> {firstRowAfter.Y}");
        Assert.True(Math.Abs(headerAfter!.Y - headerBefore!.Y) < 2,
            $"the header should have stayed put, but went {headerBefore.Y} -> {headerAfter.Y}");
    }

    protected async Task WalkUserComponentsGuideAsync()
    {
        // User components (generated factories, DI-via-ctor, [SkipFactory]) — the standalone /components
        // page was folded into the Getting started guide's factory-generation section as live demos.
        // (The DSL primitives / tag factories / universal props / SVG and the HTML-element catalog were
        // folded into the Elements guide — see WalkElementsGuideAsync.)
        await SideAsync("Getting started", "Getting started", "main .markdown-body h1");
        var greeting = Page.Locator(".guide-demo .sample-result-body p")
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


    // Composition guide: context, callbacks, virtualize, keyed lists, drag & drop, and error boundaries
    // — their standalone example pages folded into docs/composition.md as inline live demos. Open the
    // guide once and drive each demo in place; locators are scoped by unique #id or by the enclosing
    // .guide-demo (badges/result panes repeat across demos on the one page).
    protected async Task TestCompositionGuideAsync()
    {
        var contains = new LocatorAssertionsToContainTextOptions { Timeout = 10_000 };

        await SideAsync("Composition", "Composition", "main .markdown-body h1");
        Assert.True(await Page.Locator(".guide-demo .sample-card").CountAsync() >= 14,
            "expected the Composition guide to embed the demos (incl. component-tiers, the folded events, toast + master-detail) as live demos");
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

        // Component tiers: the three ways to author a unit (static method · stateless · stateful) side
        // by side. Tier 0's inlined badge and Tier 1's stateless greeting render statically; only the
        // Tier-2 counter holds state — clicking it re-renders in place with no StateHasChanged. Scoped
        // by the demo's #component-tiers container.
        var tiers = Page.Locator("#component-tiers");
        await Expect(tiers).ToContainTextAsync("inlined", contains);   // Tier 0 static helper badge
        await Expect(tiers).ToContainTextAsync("Hello, Ada", contains); // Tier 1 stateless greeting
        var tierCounter = tiers.Locator("button:has-text('Clicked')");
        await tierCounter.ClickAsync();
        await tierCounter.ClickAsync();
        await Expect(tierCounter).ToContainTextAsync("Clicked 2 times", contains);

        // Events (the full GlobalEventHandlers surface — its standalone /events page folded into this
        // guide). Scope each interaction to its own .guide-demo; result panes/inputs repeat across demos.
        var eClick = Page.Locator(".guide-demo").Filter(new LocatorFilterOptions { HasText = "Clicks:" })
            .Locator("button:has-text('Clicks:')").First;
        await eClick.ClickAsync();
        await eClick.ClickAsync();
        await Expect(eClick).ToContainTextAsync("Clicks: 2", contains);

        var eInput = Page.Locator(".guide-demo").Filter(new LocatorFilterOptions { HasText = "You typed:" });
        await eInput.Locator("input[type=text]").First.FillAsync("Hello Rask");
        await Expect(eInput).ToContainTextAsync("You typed: \"Hello Rask\"", contains);

        // Form (onSubmit → FormData): fill the named field and submit; OnSubmit reads it off a FormData and
        // echoes it. This is reliable now that the morph no longer wipes an uncontrolled input's value on a
        // full reply (it previously landed "(blank)" on the busy co-mounted guide — see the uncontrolled-input
        // reconnect guard in RunUnusualActivityAsync). The readout wraps the value in <strong>.
        var eForm = Page.Locator(".guide-demo").Filter(new LocatorFilterOptions { HasText = "Last submitted:" });
        await eForm.Locator("input[name=name]").FillAsync("Ada");
        await eForm.Locator("button[type=submit]").ClickAsync();
        await Expect(eForm).ToContainTextAsync("Last submitted: Ada", contains);

        // Full surface demo: OnDoubleClick (MouseEventArgs) + OnFocus (parameterless) reach C# and re-render
        // — proving the universal event store dispatches over both transports, not just OnClick.
        var eSurface = Page.Locator(".guide-demo").Filter(new LocatorFilterOptions { HasText = "double-clicks:" });
        await eSurface.Locator("button:has-text('Double-click')").DblClickAsync();
        await Expect(eSurface).ToContainTextAsync("double-clicks: 1", contains);
        await eSurface.Locator("div[tabindex='0']").ClickAsync();
        await Expect(eSurface).ToContainTextAsync("focused", contains);

        // Toast: a producer raises an IToaster message; the headless ToastOutlet drains it (consumed-once)
        // and renders a dismissible BsAlert — live-diff state, no client JS. The demo's ToastOutlet sets
        // AutoDismissAfter: 5s, so the toast clears itself with no click — driven entirely by a server/WASM
        // -side timer over live-diff. (Manual × dismissal is covered by the ToastOutlet unit tests.)
        await Page.Locator(".guide-demo button:has-text('Success')").First.ClickAsync();
        var toastAlert = Page.Locator(".alert:has-text('Your changes were saved.')");
        await Expect(toastAlert).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // Auto-dismiss: gone on its own within the 5s delay (+ slack for transport + render).
        await Expect(toastAlert).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 12_000 });

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

        // Master-detail (its /master-detail page folded into this section): expanding a row inserts a keyed
        // detail <tr> hosting a nested, independently sortable table; collapse removes it via the keyed diff.
        // The #md-orders / expander-{id} / inner-{id} ids are unique on the guide page.
        await Expect(Page.Locator("#md-orders tbody tr.md-row")).ToHaveCountAsync(14,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Page.Locator("[data-testid='expander-1']").ClickAsync();
        await Expect(Page.Locator("[data-testid='inner-1']")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        Assert.True(await Page.Locator("[data-testid='inner-1'] tbody tr").CountAsync() > 0,
            "expanded order should reveal line items");
        await Page.Locator("[data-testid='inner-1'] th button:has-text('Qty')").First.ClickAsync();
        await Page.WaitForTimeoutAsync(200);
        Assert.False(
            string.IsNullOrWhiteSpace(await Page.Locator("[data-testid='inner-1'] tbody tr").First
                .Locator("td").Nth(2).InnerTextAsync()),
            "inner grid should still render after sort");
        await Page.Locator("[data-testid='expander-1']").ClickAsync();
        await Expect(Page.Locator("[data-testid='inner-1']")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

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

    protected async Task WalkAuthGuideAsync()
    {
        // The BsToast element, Toast-messages and User & auth example pages were folded into their guides:
        // BsToast → WalkBootstrapGuideAsync, Toast messages + events → TestCompositionGuideAsync. This walks the
        // Authentication guide's two gating demos (imperative UserGate + declarative Authorize).
        await SideAsync("Authentication", "Authentication", "main .markdown-body h1");

        // User & auth: imperative gate (UserGate) + declarative Authorize slots, both re-rendering
        // live on IUserProvider.Changed with no reload. The demo #ids are unique on the guide page.
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
    protected async Task WalkLifecycleGuideAsync()
    {
        await SideAsync("Lifecycle", "Lifecycle", "main .markdown-body h1");
        Assert.True(await Page.Locator(".guide-demo .sample-card").CountAsync() >= 8,
            "expected the Lifecycle guide to embed the lifecycle demos (incl. the folded live ticker) as live demos");
        // Guide prose code fences are syntax-highlighted server-side (runs on every host, including
        // StandaloneWasm which can't deep-link): the ```csharp blocks carry ColorCode token spans.
        await Expect(Page.Locator("main .markdown-body pre code span[class]").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        // The guide co-mounts every lifecycle demo on one page; wait for the LAST demo's control (the
        // background-service chart, near the end) before driving any interaction so clicks never race
        // hydration on the slower transports.
        await Expect(Page.Locator("#metrics-chart svg")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        // Live ticker (its standalone /realtime/{Symbol} page folded in): the poll loop started in
        // OnMountAsync draws a zero-JS server-rendered SVG chart, and the switcher hands the ticker a new
        // Symbol (via internal state now, not a route param) so OnPropsChanged refires without a remount.
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("BTC",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Expect(Page.Locator("#ticker-chart svg")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Page.Locator("#ticker-switch-ETH").ClickAsync();
        await Expect(Page.Locator("#ticker-symbol")).ToHaveTextAsync("ETH",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#ticker-log")).ToContainTextAsync("OnPropsChanged: Symbol BTC → ETH",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

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
    protected async Task WalkRoutingGuideAsync()
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
    protected async Task WalkJsInteropGuideAsync()
    {
        await ClearJsRuntimeStorageAsync();
        await SideAsync("JavaScript interop", "JavaScript interop", "main .markdown-body h1");
        Assert.True(await Page.Locator(".guide-demo .sample-card").CountAsync() >= 8,
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
        // The copy click round-trips (handler → InvokeVoidAsync → scoped JS flashes "Copied!"); over
        // the native WebView bridge a single message can be dropped, and the copy action is idempotent,
        // so retry the click a couple of times before failing. The flash restores after 1.5s.
        var copyButton = codeCard.Locator(".sample-copy");
        for (var attempt = 1; ; attempt++)
        {
            await copyButton.ClickAsync();
            try
            {
                await Expect(copyButton).ToContainTextAsync("Copied!",
                    new LocatorAssertionsToContainTextOptions { Timeout = attempt < 3 ? 3_000 : 10_000 });
                break;
            }
            catch (PlaywrightException) when (attempt < 3)
            {
                // Bridge dropped the round-trip; click again.
            }
        }

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

        await WalkGanttDemoAsync();
    }

    // Third-party interop: the Gantt demo wraps frappe-gantt, which builds its own DOM inside a host div
    // the .NET side renders as a leaf. Only a browser can prove any of this — that the vendored library
    // loads and draws, that its events round-trip into C# state, and (the subtle one) that its DOM
    // survives a full-document morph.
    private async Task WalkGanttDemoAsync()
    {
        var demo = Page.Locator(".guide-demo:has(.rask-gantt)");
        var chart = demo.Locator(".rask-gantt");
        var bars = chart.Locator(".bar-wrapper");

        // The library loaded from the vendored _content/ asset and drew a bar per task.
        //
        // This assertion is also THE regression guard for the data-rask-managed tag Gantt.js puts on the
        // chart's DOM, and it is much sharper than it looks: the first interactive frame after page load
        // always ships full HTML, which the client applies by morphing the document. The morph pairs the
        // host's live children against the zero the .NET render declares, so without that tag the chart
        // is deleted moments after it draws and never comes back — verified by removing the tag, at which
        // point the chart never becomes visible here at all.
        await Expect(chart.Locator("svg.gantt")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Expect(bars).ToHaveCountAsync(4, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        // Exactly one container: a duplicate would mean the tag had been put on the host instead of the
        // library's children, and the morph had appended a second, empty one.
        Assert.Equal(1, await chart.Locator(".gantt-container").CountAsync());

        // The library's own stylesheet was injected into <head> at runtime and survives re-renders — the
        // behaviour this guide section documents. Assert it applied, not merely that the tag is present.
        Assert.True(
            await chart.Locator(".bar-wrapper").First.EvaluateAsync<bool>(
                "el => getComputedStyle(el.querySelector('.bar')).fill !== ''"),
            "frappe-gantt's stylesheet must be loaded and applied");

        // JS -> C#: clicking a bar routes through the static [JSInvokable] into this component's state.
        await bars.First.Locator(".bar").ClickAsync(new LocatorClickOptions { Force = true });
        await Expect(demo.Locator(".gantt-log")).ToContainTextAsync("click: Design system",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        // The full round trip: drag a bar and the C# table below re-renders with the new dates. This is
        // the half of the story a unit test can't reach — real pointer events into the library's own drag
        // handling, back out through interop, into a live re-render.
        var startCell = demo.Locator("tbody tr").First.Locator("td").Nth(1);
        var before = await startCell.TextContentAsync();
        var bar = bars.First.Locator(".bar");
        // Raw mouse events land at viewport coordinates and do NOT auto-scroll the way ClickAsync does —
        // the guide is thousands of pixels tall, so read the box only once the bar is actually on screen.
        await bar.ScrollIntoViewIfNeededAsync();
        var box = await bar.BoundingBoxAsync();
        Assert.NotNull(box);
        var cx = box!.X + (box.Width / 2);
        var cy = box.Y + (box.Height / 2);
        await Page.Mouse.MoveAsync(cx, cy);
        await Page.Mouse.DownAsync();
        // Small steps, well past the library's 10px drag threshold: it re-measures on every mousemove.
        for (var step = 1; step <= 12; step++)
        {
            await Page.Mouse.MoveAsync(cx + (step * 10), cy);
        }

        await Page.Mouse.UpAsync();
        await Expect(startCell).Not.ToHaveTextAsync(before ?? "",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await Expect(demo.Locator(".gantt-log")).ToContainTextAsync("date_change: Design system",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        // A prop change pushes the new view mode at the live instance (change_view_mode) rather than
        // re-mounting: same chart, rescaled axis. Exact names — the library renders its own "Today"
        // button, and the demo has "¼ day" / "½ day" alongside "Day".
        await demo.GetByRole(AriaRole.Button, new() { NameString = "Month", Exact = true }).ClickAsync();
        await Expect(bars).ToHaveCountAsync(4, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await demo.GetByRole(AriaRole.Button, new() { NameString = "Day", Exact = true }).ClickAsync();
        await Expect(bars).ToHaveCountAsync(4, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });

        // Add/remove push a new task list at the library. The bar count tracking the row count is what
        // proves the prop-change path: a caller who mutates the same list instance gets no prop change,
        // no OnPropsChanged, and a chart that silently stops following its data.
        await demo.Locator("button:has-text('Add task')").ClickAsync();
        await Expect(bars).ToHaveCountAsync(5, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(demo.Locator("tbody tr")).ToHaveCountAsync(5);

        for (var remaining = 5; remaining > 1; remaining--)
        {
            await demo.Locator("button:has-text('Remove last')").ClickAsync();
            await Expect(demo.Locator("tbody tr")).ToHaveCountAsync(remaining - 1,
                new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
            await Expect(bars).ToHaveCountAsync(remaining - 1,
                new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        }

        // Disabled, not removed — a sibling that disappears shifts the positional identity of every later
        // child, which would rebuild the chart component. Still exactly one chart, still one container.
        await Expect(demo.Locator("button:has-text('Remove last')")).ToBeDisabledAsync();
        await Expect(chart.Locator("svg.gantt")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        Assert.Equal(1, await chart.Locator(".gantt-container").CountAsync());
    }

    // Elements guide: the DSL primitives, tag factories, universal props, SVG, and the HTML-element
    // catalog folded into docs/elements.md (26 demos). Open the guide once, hydration-gate on a late
    // demo, then spot-check representative demos by their distinctive rendered elements.
    protected async Task WalkElementsGuideAsync()
    {
        await SideAsync("Elements & the DSL", "Elements & the DSL", "main .markdown-body h1");
        Assert.True(await Page.Locator(".guide-demo .sample-card").CountAsync() >= 26,
            "expected the Elements guide to embed the DSL/element demos as live demos");
        // Gate on a late demo's distinctive element (the Interactive-elements demo, near the end).
        await Expect(Page.Locator(".guide-demo .sample-result-body details[open] summary").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        // Primitives: Text/Raw escaping — the "Already safe" run renders a real <strong>safe</strong>.
        await Expect(Page.Locator(".guide-demo .sample-result-body p")
                .Filter(new LocatorFilterOptions { HasText = "Already" }).First.Locator("strong"))
            .ToHaveTextAsync("safe", new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });

        // Tag factories: the text-and-semantic demo renders a blockquote.
        await Expect(Page.Locator(".guide-demo .sample-result-body blockquote").First)
            .ToContainTextAsync("A small DSL", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Universal props: data-* expansion (incl. a bare null attribute) + ARIA / role / tabindex.
        var dataDiv = Page.Locator(".guide-demo .sample-result-body div[data-role='card']").First;
        await Expect(dataDiv).ToHaveAttributeAsync("data-index", "7");
        await Expect(dataDiv).ToHaveAttributeAsync("data-new", ""); // bare null attribute
        var ariaBtn = Page.Locator(".guide-demo .sample-result-body button[role='switch']").First;
        await Expect(ariaBtn).ToHaveAttributeAsync("aria-label", "Toggle dark mode");
        await Expect(ariaBtn).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(ariaBtn).ToHaveAttributeAsync("tabindex", "0");

        // HTML element catalog: spot-check distinctive elements from a few category demos.
        await Expect(Page.Locator(".guide-demo .sample-result-body ruby").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator(".guide-demo .sample-result-body ol[start='2'][reversed]").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator(".guide-demo .sample-result-body meter").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // SVG: the shapes demo renders a live <svg> (typed factories, no Raw()).
        await Expect(Page.Locator(".guide-demo .sample-result-body svg").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    protected async Task WalkHttpAndFilesGuideAsync()
    {
        // The HttpClient+DI, file-upload and file-download example pages were folded into
        // docs/http-and-files.md as inline live demos. Drive the guide and assert each demo mounted.
        await SideAsync("HTTP & files", "HTTP & files", "main .markdown-body h1");
        Assert.True(await Page.Locator(".guide-demo .sample-card").CountAsync() >= 4,
            "expected the HTTP & files guide to embed the http/upload/download demos as live demos");

        // HttpClient + DI: the injected client loads a post card in OnMountAsync. This also guards the
        // WASM base-address fix — the relative fetch must resolve against the app root from the two-segment
        // /guides/http-and-files route (not against /guides/), or it 404s and the error banner shows instead.
        await Expect(Page.Locator(".guide-demo .sample-result-body article.card").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        // File upload: the typed file picker renders its input.
        await Expect(Page.Locator(".guide-demo .sample-result-body #upload-input").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // File download: the download button renders (the download sink itself is unit-tested).
        await Expect(Page.Locator(".guide-demo .sample-result-body #download-report").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    protected async Task WalkFormsPagesAsync()
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
        var aiChip = multi.Locator(".badge").Filter(new LocatorFilterOptions { HasText = "AI" });
        await Expect(aiChip).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Removing a chip via its × must drop the badge on its own — no reopening the dropdown. The × is a
        // BsCloseButton (a wrapper) callback, so the GENERIC BsMultiSelect<T> re-renders only if AutoCallback
        // resolves it as the owner; a regression there left the badge onscreen until an unrelated render.
        // Close the dropdown first (its full-viewport backdrop otherwise intercepts the ×, and a closed
        // dropdown is the exact scenario the bug was reported in — the badge lingered until reopening).
        await multi.Locator(".position-fixed").DispatchEventAsync("click");
        await Expect(Page.Locator("#ms-interests .dropdown-menu.show")).ToBeHiddenAsync(
            new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });
        await aiChip.Locator(".btn-close").ClickAsync();
        await Expect(aiChip).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        // Re-open and re-select so the open-menu / Escape / backdrop steps below still exercise a filled control.
        await multi.Locator(".form-select").ClickAsync();
        await multi.Locator(".dropdown-item").Filter(new LocatorFilterOptions { HasText = "AI" }).ClickAsync();
        await Expect(aiChip).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // The menu stays open across selections; Escape from the focusable box closes it (no bootstrap.js).
        // (Type-to-filter is opt-in via a Filter predicate and shares BsSelect's dropdown search field, which
        // is exercised on #bs-plan above.)
        var openMenu = Page.Locator("#ms-interests .dropdown-menu.show");
        await Expect(openMenu).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // Same overflow-escape helper: the open menu is re-anchored position:fixed (data-rask-popover).
        Assert.Equal("fixed", await openMenu.EvaluateAsync<string>("el => getComputedStyle(el).position"));
        await multi.Locator(".form-select").FocusAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(openMenu).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });

        // Re-open, then close by clicking outside — the transparent full-viewport backdrop (z-index 999)
        // catches it. Dispatch the click straight to the backdrop element rather than a coordinate click:
        // a positional click is unreliable here (the sticky navbar covers the top, and — now that
        // CodeSample stacks full-width — the `w-100` open menu covers the centre band, so both intercept
        // parts of the backdrop). DispatchEvent bubbles to Rask's delegated click handler and fires the
        // backdrop's OnClick(_open=false), exercising the close-on-outside-click path deterministically.
        await multi.Locator(".form-select").ClickAsync();
        await Expect(openMenu).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await multi.Locator(".position-fixed").DispatchEventAsync("click");
        await Expect(openMenu).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });

        // Controlled BsMultiSelect (Value + OnChange, no Bind): selecting a topic flows out through OnChange
        // and the parent's summary updates — again with no StateHasChanged.
        var controlled = Page.Locator("#ms-controlled");
        await controlled.Locator(".form-select").ClickAsync();
        await controlled.Locator(".dropdown-item").Filter(new LocatorFilterOptions { HasText = "Tech" }).ClickAsync();
        await Expect(Page.Locator("#ms-controlled-summary")).ToContainTextAsync("Tech",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        // Close it again so the open dropdown's full-viewport backdrop doesn't intercept later navigation.
        // DispatchEvent (not a positional click) — the full-width open menu / sticky navbar cover the
        // backdrop's clickable points; this fires the backdrop's OnClick handler directly. See the
        // #ms-interests close above.
        await controlled.Locator(".position-fixed").DispatchEventAsync("click");
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
        // The bound group passes Label:, so the radios are wrapped in an accessible <fieldset>/<legend>.
        await Expect(Page.Locator("#fc-radio-bound fieldset > legend")).ToHaveTextAsync("Plan",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
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
        await fcMulti.Locator(".position-fixed").DispatchEventAsync("click");
        await Expect(fcMulti.Locator(".dropdown-menu.show")).ToBeHiddenAsync(
            new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });
    }

    protected async Task WalkStylingDataAndAppPagesAsync()
    {
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
        // and on desktop the sidebar's list (.side-nav-scroll) becomes an independent, viewport-bounded
        // scroll region so it scrolls inside itself rather than stretching the page — the "navbar too
        // tall" fix. The filter above it is a pinned flex header (the body itself does not scroll — a
        // sticky-in-flex child would not stick in Safari). (Groups now collapse by default, so the list
        // itself is short; this only asserts the region is bounded and scrollable when needed.)
        Assert.Equal("56px", (await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.documentElement).getPropertyValue('--nav-h').trim()")));
        var navScroll = await Page.Locator(".side-nav .side-nav-scroll").First.EvaluateAsync<string>(
            @"el => {
                const cs = getComputedStyle(el);
                const body = getComputedStyle(el.closest('.offcanvas-body'));
                const navH = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--nav-h'));
                return JSON.stringify({
                    overflowY: cs.overflowY,
                    bodyOverflowY: body.overflowY,
                    bounded: el.clientHeight <= window.innerHeight - navH + 1,
                });
            }");
        Assert.Contains("\"overflowY\":\"auto\"", navScroll);
        // The body itself must not scroll — only the inner list does, so the filter stays pinned.
        Assert.Contains("\"bodyOverflowY\":\"hidden\"", navScroll);
        Assert.Contains("\"bounded\":true", navScroll);

        // HttpClient + DI, file upload and file download moved to the HTTP & files guide
        // (WalkHttpAndFilesGuideAsync).

        // Todos: full CRUD + URL-driven dialog. Add, edit, toggle, delete.
        await SideAsync("Todos", "Todos");
        await Expect(Page.Locator(".list-group .list-group-item")).ToHaveCountAsync(2,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Page.Locator("button:has-text('New todo')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos/new$"),
            new PageAssertionsToHaveURLOptions { Timeout = 15_000 });
        // BsModal opens centered over a .modal-backdrop; clicking the modal area outside the dialog cancels.
        await Expect(Page.Locator(".modal.show")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Expect(Page.Locator(".modal-backdrop")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        // BsModal's focus trap moves focus into the dialog on open (the .modal itself carries tabindex=-1),
        // so the keyboard primitive works with no prior click.
        await Expect(Page.Locator(".modal.show")).ToBeFocusedAsync(
            new LocatorAssertionsToBeFocusedOptions { Timeout = 15_000 });
        // Escape closes the modal: the runtime focus trap routes Escape to the dismiss target (OnClose → cancel).
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos$"),
            new PageAssertionsToHaveURLOptions { Timeout = 15_000 });
        // Reopen, then dismiss via the Cancel button (BsModal's backdrop/close-button dismiss mechanics are
        // covered by the Bootstrap modal demo E2E; here we just need a reliable route-driven close).
        await Page.Locator("button:has-text('New todo')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos/new$"),
            new PageAssertionsToHaveURLOptions { Timeout = 15_000 });
        await Expect(Page.Locator(".modal.show")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Page.Locator(".modal button:has-text('Cancel')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos$"),
            new PageAssertionsToHaveURLOptions { Timeout = 15_000 });
        // Reopen for the rest of the flow.
        await Page.Locator("button:has-text('New todo')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos/new$"),
            new PageAssertionsToHaveURLOptions { Timeout = 15_000 });
        // Empty submit → [Required].
        await Page.Locator("button:has-text('Add')").ClickAsync();
        await Expect(Page.Locator(".invalid-feedback")).ToContainTextAsync("Title is required",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Page.Locator("#todo-title").FillAsync("Wire up reconnect");
        await Page.Locator("button:has-text('Add')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos$"),
            new PageAssertionsToHaveURLOptions { Timeout = 15_000 });
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
    protected async Task TestBrowserApisAsync()
    {
        var contains = new LocatorAssertionsToContainTextOptions { Timeout = 10_000 };
        var visible = new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 };

        // The Browser APIs guide co-mounts every typed wrapper as a LIVE demo on one page (the child
        // enumerable is materialised at render time so each demo's component instance is reconciled and
        // keeps its state across renders — see Component's IEnumerable<Component> indexer). Open the guide,
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
        // Speech recognition needs a real microphone (and Chromium's cloud recognizer) — can't round-trip
        // headless, so smoke-check the control renders.
        await Expect(Page.Locator("#speech-recognize-start")).ToBeVisibleAsync(visible);

        // Broadcast channel — full JS→C# push round-trip (BroadcastChannel.onmessage → [JSInvokable] →
        // handler → StateHasChanged), on every host including trimmed WASM.
        await Page.Locator("#bc-send").ClickAsync();
        await Expect(Page.Locator("#bc-log")).ToContainTextAsync("Message #1", contains);

        // Web Locks — a full C#→JS→C# round-trip that runs headlessly (navigator.locks needs no permission
        // or gesture): TryRequestAsync acquires the free lock, runs the callback, and releases, all through
        // the __raskLocks helper under a C#-minted id.
        await Page.Locator("#locks-try").ClickAsync();
        await Expect(Page.Locator("#locks-status")).ToContainTextAsync("acquired", contains);

        // Battery — one-shot read of navigator.getBattery (headless Chromium provides a mock manager, so
        // GetStatusAsync resolves rather than returning null); the status flips to "read".
        await Page.Locator("#battery-read").ClickAsync();
        await Expect(Page.Locator("#battery-status")).ToContainTextAsync("read", contains);

        // Intersection observer — another push: scroll the target in and the browser pushes the change.
        await Expect(Page.Locator("#io-status")).ToContainTextAsync("out of view", contains);
        await Page.Locator("#io-target").ScrollIntoViewIfNeededAsync();
        await Expect(Page.Locator("#io-status")).ToContainTextAsync("in view", contains);

        // Gesture bridge (GestureBridgeDemo) — the declarative triggers stamp a data-rask-gesture attribute
        // that runs an activation-gated API inside the click, so they work on this Server host too. The
        // gestures themselves need a real display / permission and can't fire headlessly (same ceiling as
        // fullscreen/eyedropper), so assert the wiring is present, not the effect.
        var gestureAttr = new LocatorAssertionsToHaveAttributeOptions { Timeout = 10_000 };
        await Expect(Page.Locator("#orientation-btn")).ToHaveAttributeAsync(
            "data-rask-gesture", new Regex("orientation\\.lock"), gestureAttr);
        await Expect(Page.Locator("#install-btn")).ToHaveAttributeAsync(
            "data-rask-gesture", new Regex("install\\.prompt"), gestureAttr);
        await Expect(Page.Locator("#camera-btn")).ToHaveAttributeAsync(
            "data-rask-gesture", new Regex("media\\.start"), gestureAttr);
        await Expect(Page.Locator("#pip-btn")).ToHaveAttributeAsync(
            "data-rask-gesture", new Regex("pip\\.request"), gestureAttr);
    }

    protected async Task TestInSessionNotFoundAsync()
    {
        await Page.EvaluateAsync(@"() => {
            history.pushState({ rask: true }, '', '/in-session-missing');
            window.dispatchEvent(new PopStateEvent('popstate'));
        }");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Page not found",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator(".side-nav a.side-nav-link.active")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        // "Back to guides" is an in-session nav to "/" — returns us to a known page so the journey
        // can continue, and proves recovery from the not-found state.
        await Page.Locator("main button:has-text(\"Back to guides\")").ClickAsync();
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Guides",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
    }

    // ---- unusual user activity -----------------------------------------------------------------

    protected async Task RunUnusualActivityAsync(ShowcaseJourneyOptions opts)
    {
        // Back / forward: history navigation must preserve the SPA sentinel and resolve both ends.
        await SideAsync("Todos", "Todos");
        await Page.GoBackAsync();
        Assert.Equal("alive", await Page.EvaluateAsync<string?>("() => window.__raskSentinel"));
        await Page.GoForwardAsync();
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Todos",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });

        // Data table: a [QueryParam]-driven grid — every interaction is a URL query mutation → rebind →
        // re-render. Its /table route is unlisted (folded code-only into routing.md) but still a real page;
        // exercise it here (post-sentinel) since reaching it is a hard nav. Sort → ?sort=name, filter →
        // ?filter=…, page-size → 25 rows.
        await Page.GotoAsync("/table");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Data table",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Expect(Page.Locator("tbody tr")).ToHaveCountAsync(10,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Page.Locator("th button:has-text('Name')").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*[\\?&]sort=name"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        await Page.Locator("input[type='search']").FillAsync("Linus");
        await Page.WaitForTimeoutAsync(300);
        await Expect(Page).ToHaveURLAsync(new Regex(".*[\\?&]filter=Linus"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        var filteredRows = await Page.Locator("tbody tr").CountAsync();
        Assert.True(filteredRows is > 0 and < 10, $"filter should reduce rows; got {filteredRows}");
        await Page.Locator("input[type='search']").FillAsync("");
        await Page.Locator("select.form-select-sm").SelectOptionAsync("25");
        await Expect(Page.Locator("tbody tr")).ToHaveCountAsync(25,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

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

            // Guide prose code fences are syntax-highlighted server-side (Markdig has no highlighter, so
            // Markdown.HighlightCodeBlocks runs ColorCode) — a fresh load must carry token spans.
            await Page.GotoAsync("/guides/getting-started");
            await Expect(Page.Locator("main .markdown-body h1")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
            await Expect(Page.Locator("main .markdown-body pre code[class*='language-']:has(span[class])").First)
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
            // Deep link with a #fragment: on a hard load, GuideChrome.scrollToHash must scroll to the
            // section (not sit at the top). Use a real late-heading id so the anchor always exists.
            var deepAnchor = await Page.Locator(".markdown-body :is(h2, h3)[id]").Last.GetAttributeAsync("id");
            Assert.False(string.IsNullOrEmpty(deepAnchor), "no anchored heading to deep-link to");
            await Page.GotoAsync($"/guides/getting-started#{deepAnchor}");
            await Expect(Page.Locator("main .markdown-body h1")).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
            await Page.WaitForFunctionAsync("() => window.scrollY > 0",
                null, new PageWaitForFunctionOptions { Timeout = 10_000 });

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
            await Expect(Page.Locator("main h1.h2"))
                .ToContainTextAsync("Guides",
                    new LocatorAssertionsToContainTextOptions { Timeout = 60_000 });
        }

        if (opts.Slow3g)
        {
            // Emulate a slow link via Chromium CDP and confirm the HTTP demo on the HTTP & files guide
            // still settles (a hard nav to the two-segment /guides/http-and-files route also exercises the
            // WASM base-address fix under throttling). Then restore full speed so later steps aren't penalized.
            var cdp = await Page.Context.NewCDPSessionAsync(Page);
            await cdp.SendAsync("Network.emulateNetworkConditions", new Dictionary<string, object>
            {
                ["offline"] = false,
                ["latency"] = 400,
                ["downloadThroughput"] = 50 * 1024,
                ["uploadThroughput"] = 50 * 1024,
            });
            await Page.GotoAsync("/guides/http-and-files");
            await Expect(Page.Locator(".guide-demo .sample-result-body article.card").First).ToBeVisibleAsync(
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
                await Page.GotoAsync("/guides/composition");
                var bump = Page.Locator(".guide-demo .sample-result-body button:has-text('Clicks:')").First;
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
            // Drop and restore the WebSocket; server-held state must survive the reconnect. The events
            // click-counter demo now lives on the Composition guide (its /events page was folded in).
            await Page.GotoAsync("/guides/composition");
            await Expect(Page.Locator("main .markdown-body h1")).ToContainTextAsync("Composition",
                new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
            var clicks = Page.Locator(".guide-demo .sample-result-body button:has-text('Clicks:')").First;
            await clicks.ClickAsync();
            await clicks.ClickAsync();
            await Expect(clicks).ToContainTextAsync("Clicks: 2",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
            // An *uncontrolled* input's value is client-owned (the framework renders no `value` attribute
            // for it). A reconnect forces a full-HTML resend that morphs the whole document — and that
            // morph must NOT reset the uncontrolled input to "" (regression: it did, wiping any in-progress
            // typed value on every full reply — scoped-CSS delivery, reconnect, …). The events-form demo's
            // name field is uncontrolled; type into it, then reconnect and assert the value survived.
            var uncontrolled = Page.Locator(".guide-demo")
                .Filter(new LocatorFilterOptions { HasText = "Last submitted:" }).Locator("input[name=name]");
            await uncontrolled.FillAsync("survive-the-reconnect");
            await Page.Context.SetOfflineAsync(true);
            await Page.Context.SetOfflineAsync(false);
            await clicks.ClickAsync();
            await Expect(clicks).ToContainTextAsync("Clicks: 3",
                new LocatorAssertionsToContainTextOptions { Timeout = 15_000 }); // server-held state survived
            await Expect(uncontrolled).ToHaveValueAsync("survive-the-reconnect",
                new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });   // client-owned value survived the resync morph
        }

        // Memory: a stress loop of in-SPA navigations must not balloon the JS heap.
        var baseline = await SampleJsHeapAsync();
        var labels = new[] { "Composition", "Getting started", "JavaScript interop", "Routing", "Browser APIs" };
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
    protected async Task AssertNavigationScrollAsync()
    {
        // --- a forward nav resets scroll to the top ---------------------------------------------
        // The data table at 25 rows is reliably taller than the viewport; scroll to the bottom and
        // confirm the document actually moved before navigating away. (Its /table route is unlisted now —
        // folded code-only into routing.md — so reach it directly.)
        await Page.GotoAsync("/table");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Data table",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Page.Locator("select.form-select-sm").SelectOptionAsync("25");
        await Expect(Page.Locator("tbody tr")).ToHaveCountAsync(25,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Page.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");
        await Page.WaitForFunctionAsync("() => window.scrollY > 0",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        await SideAsync("Todos", "Todos");
        // The new page must land at the top (the reset can lag a CSS-deferred body commit, so poll).
        await Page.WaitForFunctionAsync("() => Math.round(window.scrollY) === 0",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // --- a data-rask-nav link with a #fragment scrolls to that element ----------------------
        // The showcase navigates via sidebar buttons, so inject a real NavLink-style anchor to drive
        // the click-interceptor + fragment path. The Routing guide is a long page and its last section
        // (#not-found-and-auth-gating, an AutoIdentifiers heading anchor) sits well below the fold, so
        // reaching it must move the scroll.
        await SideAsync("All guides", "Guides");
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

    protected async Task HtmlDragDropAsync(string sourceSelector, string targetSelector)
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

    protected async Task ClearJsRuntimeStorageAsync()
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

    protected async Task WaitForHighlightedSpansAsync(int timeoutMs)
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
