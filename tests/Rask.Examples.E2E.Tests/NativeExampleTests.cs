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

        // The same shared showcase walks the browser hosts run. With the native concurrent-render race fixed
        // (NativeLiveSession._renderLock serializes renders that HandlerSyncContext fans onto the thread
        // pool), native drives the full set reliably — including the JS-interop element-ref focus +
        // sessionStorage round-trip, the URL-routed Todos dialog + Browser-APIs co-mount, and the popstate
        // in-session 404.
        await TestSidebarNavAsync();
        await WalkUserComponentsGuideAsync();
        await TestCompositionGuideAsync();
        await WalkLifecycleGuideAsync();
        await WalkRoutingGuideAsync();
        await WalkJsInteropGuideAsync();
        await WalkElementsGuideAsync();
        await WalkCqrsGuideAsync();
        await WalkFormsPagesAsync();
        await WalkStylingDataAndAppPagesAsync();
        await WalkBootstrapGuideAsync();
        await TestGuidesAsync();
        await TestInSessionNotFoundAsync();

        // Not exercised: the HTTP & files guide — its DI'd HttpClient fetch runs in the .NET host (Playwright
        // can't intercept it), and native file upload/download has no bridge yet (docs/native.md follow-up).

        Assert.Equal("alive", await Page.EvaluateAsync<string?>("() => window.__raskSentinel"));
    }
}
