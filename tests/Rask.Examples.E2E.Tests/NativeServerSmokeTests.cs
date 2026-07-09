using Rask.Examples.E2E.Tests.Infrastructure;
using Rask.Native;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Native + Server smoke. Unlike Native + Local (NativeExampleTests, in-process), the Native + Server mode
// is a THIN native shell: NativeAppHost.ConnectToServer(uri) validates the target, and the platform head
// just points its WebView at that remote Rask Server — which serves its own rask.js and speaks the normal
// WebSocket protocol. There's no native client or in-process session involved.
//
// So this is a smoke, not the full journey: it asserts the ConnectToServer contract, then loads the shell
// URL in a mobile-emulated Chromium context (the faithful headless proxy for a device WebView, same idea
// as the Native + Local shim) and confirms the Server app renders and reacts live over the socket. The
// exhaustive Server-transport coverage lives in ServerExampleTests; the device WebView chrome itself is a
// device-only concern.
[Collection(NativeServerSmokeCollection.Name)]
public sealed class NativeServerSmokeTests(NativeServerSmokeAppFixture app, PlaywrightFixture pw)
{
    [Fact]
    public async Task NativeServer_thin_shell_renders_and_reacts_over_websocket()
    {
        // Contract: the target must be an absolute URL (a device head would otherwise have nothing to load).
        Assert.Throws<ArgumentException>(() => NativeAppHost.ConnectToServer(new Uri("/app", UriKind.Relative)));
        var shell = NativeAppHost.ConnectToServer(new Uri(app.BaseUrl));
        Assert.Equal(app.BaseUrl.TrimEnd('/'), shell.ServerBaseUrl.ToString().TrimEnd('/'));

        // Represent the device WebView with a mobile-emulated context pointed at the shell URL.
        await using var ctx = await pw.Browser.NewContextAsync(new()
        {
            BaseURL = app.BaseUrl,
            ViewportSize = new() { Width = 390, Height = 844 },
            UserAgent = "Mozilla/5.0 (Linux; Android 15; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) " +
                        "Chrome/131.0.0.0 Mobile Safari/537.36"
        });
        var page = await ctx.NewPageAsync();
        await page.GotoAsync(shell.ServerBaseUrl.ToString());

        // The Server showcase rendered inside the WebView-shaped context (its runtime <script> booted).
        await Expect(page.Locator("h1.display-5")).ToContainTextAsync("The Rask framework",
            new() { Timeout = 60_000 });

        // Live over the WebSocket: an in-SPA navigation must diff a new page in without a full reload.
        await page.EvaluateAsync("() => { window.__raskServerSmoke = 'alive'; }");
        // On the phone viewport the sidebar is an offcanvas drawer behind the hamburger — open it first.
        await page.Locator(".hamburger-btn").ClickAsync();
        await Expect(page.Locator(".side-nav")).ToBeInViewportAsync(new() { Timeout = 10_000 });
        await page.Locator(".side-nav .side-nav-filter").FillAsync("Lifecycle");
        var link = page.Locator(".side-nav a.side-nav-link:has-text(\"Lifecycle\")").First;
        await link.WaitForAsync(new() { Timeout = 15_000 });
        await link.ClickAsync();
        await Expect(page.Locator("main .markdown-body h1, main h1.h2").First)
            .ToContainTextAsync("Lifecycle", new() { Timeout = 30_000 });
        Assert.Equal("alive", await page.EvaluateAsync<string?>("() => window.__raskServerSmoke"));
    }
}
