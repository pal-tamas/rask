using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// IJSRuntime + sessionStorage round-trip. Lives in SharedSmokeTests so it
// exercises Server (WS-bound RaskJSRuntime), Wasm.Host (in-process bridge),
// and StandaloneWasm. The unified IJSRuntime surface (commit 87f2b04) makes
// the same component code work on every host — these tests assert the
// runtime end-to-end behaviour matches across them.
public abstract partial class SharedSmokeTests
{
    [Fact]
    public Task JsRuntime_FirstMount_ShowsNoValueStatus() => RunAsync(async () =>
    {
        // OnRenderedAsync(firstRender:true) calls js.InvokeAsync<string?>(
        // "sessionStorage.getItem", "rask.jsruntime.demo"). On a fresh session
        // there's no value, so the status renders "(no value yet — try Set)".
        // If OnRenderedAsync never fires (a known regression class) the status
        // stays at "(idle)".
        await ClearJsRuntimeStorageAsync();
        await NavigateToAsync("/jsruntime");
        await Expect(Page.Locator("main h1.h2")).ToContainTextAsync("IJSRuntime",
            new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });

        await Expect(Page.Locator("#demo-status")).ToContainTextAsync("no value yet",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    });

    [Fact]
    public Task JsRuntime_SetThenRead_RoundTripsValue() => RunAsync(async () =>
    {
        // Full round-trip: type a value, Set writes via InvokeVoidAsync, Read
        // pulls it back via InvokeAsync<string?>. Status mirrors each step.
        await ClearJsRuntimeStorageAsync();
        await NavigateToAsync("/jsruntime");
        await Expect(Page.Locator("main h1.h2")).ToContainTextAsync("IJSRuntime",
            new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });

        await Page.Locator("#demo-input").FillAsync("hello-rask");
        await Page.Locator("#demo-set").ClickAsync();
        await Expect(Page.Locator("#demo-status")).ToContainTextAsync("Set to: hello-rask",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await Page.Locator("#demo-read").ClickAsync();
        await Expect(Page.Locator("#demo-last-read")).ToHaveTextAsync("hello-rask",
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#demo-status")).ToContainTextAsync("Read: hello-rask",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task JsRuntime_Remove_ClearsStoredValue() => RunAsync(async () =>
    {
        await ClearJsRuntimeStorageAsync();
        await NavigateToAsync("/jsruntime");
        await Expect(Page.Locator("main h1.h2")).ToContainTextAsync("IJSRuntime",
            new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });

        await Page.Locator("#demo-input").FillAsync("temp");
        await Page.Locator("#demo-set").ClickAsync();
        await Expect(Page.Locator("#demo-status")).ToContainTextAsync("Set to: temp",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await Page.Locator("#demo-remove").ClickAsync();
        await Expect(Page.Locator("#demo-status")).ToContainTextAsync("Removed",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#demo-last-read")).ToHaveTextAsync("(null)",
            new LocatorAssertionsToHaveTextOptions { Timeout = 5_000 });

        // Confirm the underlying storage really was cleared (independent of UI).
        var raw = await Page.EvaluateAsync<string?>(
            "() => sessionStorage.getItem('rask.jsruntime.demo')");
        Assert.Null(raw);
    });

    private async Task ClearJsRuntimeStorageAsync()
    {
        // Helper: clear the storage key from prior test runs. We do this from
        // the page context rather than via IJSRuntime to keep setup independent
        // of the framework's runtime correctness.
        try
        {
            await Page.EvaluateAsync(
                "() => { try { sessionStorage.removeItem('rask.jsruntime.demo'); } catch (_) {} }");
        }
        catch
        {
            // First time setup — no page loaded yet, eval will throw. Ignore.
        }
    }
}
