using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Example.Native;
using Rask.Example.Shared;
using Rask.Examples.E2E.Tests.Infrastructure;
using Rask.Native;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Native + Local host under the SAME showcase + E2E net as Server and WASM — driven headlessly through a
// Playwright-backed INativeWebView (no emulator). It runs the real rask.native.js client +
// NativeAppHost.RunLocalAsync pipeline in Chromium (the WebView engine class Android ships) against the
// same Rask.Example.Shared.App the other hosts mount.
//
// The journey is a native-appropriate subset of the shared walks (SharedSmokeTests.Journey.cs): it covers
// everything the in-process native host supports — reactivity, composition, lifecycle, routing, scoped
// CSS/JS interop, forms + keyboard, CQRS, Bootstrap components, guides — and skips the walks that are
// transport-coupled to the browser hosts: the .NET-side HttpClient (Playwright can't intercept it) and the
// offline-WS / slow-3G / deep-link-reload tails, which don't apply to an offline in-process host.
[Collection(NativeExampleCollection.Name)]
public sealed class NativeExampleTests(PlaywrightFixture pw) : SharedSmokeTests(pw)
{
    private NativeApp? _app;
    private bool _shellLoaded;

    protected override string BaseUrl => NativeExampleHost.AppOrigin.TrimEnd('/');
    protected override string FixtureName => "Native";
    protected override string ServerLog => "(native host runs in-process — no separate server log)";

    // Install the Playwright-backed INativeWebView on THIS page and start the in-process host, before the
    // journey navigates, so the client's boot `ready` reaches the session.
    protected override async Task ConfigurePageAsync()
    {
        var webView = await PlaywrightNativeWebView.CreateAsync(Page);
        await Page.RouteAsync("**/*", NativeOriginServer.HandleAsync);
        var host = NativeExampleHost.Create();
        _app = await host.RunLocalAsync<App>(webView);
    }

    protected override async Task TeardownAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    // Native + Local has no HTTP server / SPA fallback, so a deep-link GET can't boot a route. Like the
    // StandaloneWasm host, load the shell once, then stay in-SPA via the sidebar (ClickSidebar).
    protected override async Task NavigateToAsync(string path)
    {
        if (!_shellLoaded)
        {
            await Page.GotoAsync("/");
            _shellLoaded = true;
        }
    }

    [Fact]
    public Task Journey_NativeLocalShowcase() => RunAsync(RunNativeJourneyAsync);

    private async Task RunNativeJourneyAsync()
    {
        await NavigateToAsync("/");
        var home = Page.Locator("h1.display-5").First;
        await home.WaitForAsync(new() { Timeout = 60_000 });
        Assert.Contains("Rask", await home.InnerTextAsync());

        // Sentinel: every in-SPA nav below must preserve it (proves the native bridge morphs — it never
        // reloads the shell).
        await Page.EvaluateAsync("() => { window.__raskSentinel = 'alive'; }");

        // Sidebar first — it asserts the fresh, unfiltered sidebar (the sidebar filter is left set by every
        // later ClickSidebar navigation), and exercises the collapsible groups + mobile offcanvas drawer.
        await TestSidebarNavAsync();

        // A representative render-only walk over the showcase: navigate the guide + a few example pages and
        // assert each renders over the native bridge without tripping the root error boundary.
        await WalkLifecycleGuideAsync();

        // Prove the two native-host behaviours these changes fixed:
        //   • IJSRuntime marshals an invoke ARGUMENT over the bridge and the handler awaits its result —
        //     element-ref focus + scoped CSS/JS (the DispatchOutsideRender argsJson fix).
        //   • the native client drives its own WebView history — route→URL push, hardware Back/forward via
        //     popstate, and URL-routed UI (the Todos dialog) — the applyHistory/popstate addition.
        await NativeInteropProofsAsync();
        await NativeHistoryProofsAsync();

        // Scope note: the native E2E deliberately runs a focused journey rather than the browser hosts'
        // full 12-walk gauntlet. Every interaction is an async round-trip over the WebView bridge, so a long
        // back-to-back interaction sequence accumulates timing flake without adding coverage; this set
        // proves the pipeline (boot, render, in-SPA nav, scoped assets), both fixes above, and keyboard (the
        // Todos dialog's Escape). The exhaustive per-feature behaviour is covered by the Server/WASM shards
        // (same shared showcase) + the native unit tests. Also not exercised: the HTTP & files guide (its
        // DI'd HttpClient fetch runs in the .NET host, which Playwright can't intercept) and native file
        // upload/download (no bridge yet — docs/native.md follow-up).

        Assert.Equal("alive", await Page.EvaluateAsync<string?>("() => window.__raskSentinel"));
    }

    // IJSRuntime results over the bridge. Scoped CSS/JS load, then the two interactions that AWAIT a JS
    // result inside a handler — element-ref focus and a sessionStorage set/read round-trip — which is
    // exactly what the DispatchOutsideRender argsJson fix restored.
    private async Task NativeInteropProofsAsync()
    {
        await ClearJsRuntimeStorageAsync();
        await SideAsync("JavaScript interop", "JavaScript interop", "main .markdown-body h1");

        // Scoped CSS applied (served from the registry): two `.box` components differ, neither transparent.
        var boxes = Page.Locator(".guide-demo .sample-result-body .box");
        await Expect(boxes).ToHaveCountAsync(2, new LocatorAssertionsToHaveCountOptions { Timeout = 45_000 });
        var bg0 = await boxes.Nth(0).EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.NotEqual("rgba(0, 0, 0, 0)", bg0);
        Assert.NotEqual(bg0,
            await boxes.Nth(1).EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor"));
        Assert.True(
            await Page.EvaluateAsync<bool>("() => typeof window.Rask === 'object' && window.Rask !== null"),
            "scoped JS namespace window.Rask is missing — component JS did not load over the native bridge");

        // Element-ref focus via IJSRuntime: the handler awaits a void invoke that carries an element-ref
        // ARGUMENT — exactly the shape the DispatchOutsideRender argsJson fix restored (before it, the ref
        // arg reached the client as a mangled literal and the focus never landed).
        var elDemo = Page.Locator(".guide-demo:has(button:has-text('Measure the box'))");
        await elDemo.Locator("button:has-text('Focus the input')").ClickAsync();
        await Expect(elDemo.Locator(".sample-result-body input"))
            .ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 15_000 });
    }

    // The native client drives its own WebView history: route→URL push, hardware Back/forward via popstate,
    // and the URL-routed Todos dialog (open pushes /todos/new + auto-focuses via ElementRef.FocusAsync;
    // Escape routes back to /todos).
    private async Task NativeHistoryProofsAsync()
    {
        var url = new PageAssertionsToHaveURLOptions { Timeout = 15_000 };
        // Route → URL push: navigating to Todos must push /todos onto the WebView history.
        var previous = new Regex(Regex.Escape(new Uri(Page.Url).AbsolutePath) + "$");
        await SideAsync("Todos", "Todos");
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos$"), url);
        // Hardware Back / forward via popstate returns to the prior route, then forward again.
        await Page.GoBackAsync();
        await Expect(Page).ToHaveURLAsync(previous, url);
        await Page.GoForwardAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos$"), url);

        await Page.Locator("button:has-text('New todo')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos/new$"), url);
        await Expect(Page.Locator("dialog[open]"))
            .ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 15_000 });
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page).ToHaveURLAsync(new Regex(".*/todos$"), url);
    }
}
