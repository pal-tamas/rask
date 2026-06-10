using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// JsRuntime page-reload test. Lives in ExampleSmokeTests because StandaloneWasm
// (WasmAppHost) has no SPA fallback — Page.ReloadAsync() on a non-/index.html
// URL returns 404. Server and Wasm.Host both install fallbacks so the reload
// resolves cleanly.
public abstract partial class ExampleSmokeTests
{
    [Fact]
    public Task JsRuntime_Reload_OnRenderedAsync_AutoReadsPersistedValue() => RunAsync(async () =>
    {
        // Production-critical: OnRenderedAsync(firstRender:true) must run on
        // every page mount, including the mount that happens after a full
        // page reload. This proves IJSRuntime works inside lifecycle hooks
        // AND that the hook fires reliably on every new mount.
        await Page.EvaluateAsync(
            "() => { try { sessionStorage.removeItem('rask.jsruntime.demo'); } catch (_) {} }").ContinueWith(_ => { });
        await Page.GotoAsync("/jsruntime");
        await Expect(Page.Locator("main h1.h2")).ToContainTextAsync("IJSRuntime",
            new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });

        await Page.Locator("#demo-input").FillAsync("after-reload");
        await Page.Locator("#demo-set").ClickAsync();
        await Expect(Page.Locator("#demo-status")).ToContainTextAsync("Set to: after-reload",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await Page.ReloadAsync();
        await Expect(Page.Locator("main h1.h2")).ToContainTextAsync("IJSRuntime",
            new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });

        await Expect(Page.Locator("#demo-status")).ToContainTextAsync("Read on mount: after-reload",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator("#demo-last-read")).ToHaveTextAsync("after-reload",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
    });
}
