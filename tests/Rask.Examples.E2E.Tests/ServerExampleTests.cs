using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

// The Server host's single browser journey. ASP.NET host with a SPA fallback and a live
// WebSocket, so it runs every step: deep-link + refresh, slow-3G throttling, and the
// offline→online WebSocket reconnect that preserves server-held session state.
[Collection(ServerExampleCollection.Name)]
public sealed class ServerExampleTests(ServerExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Server";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task Journey_WalksEveryPageAndUnusualActivity() => RunAsync(() =>
        RunShowcaseJourneyAsync(new ShowcaseJourneyOptions
        {
            DeepLink = true,
            OfflineReconnect = true,
            Slow3g = true,
        }));

    // Server PWA: the app is installable (manifest linked + served with app-rooted URLs, SW
    // auto-registers) and the Server PWA showcase page renders. The critical assertion is the offline
    // fallback — an offline navigation must serve the static offline.html, NOT a dead cached session
    // shell (the Server SW deliberately drops the WASM app-shell navigation cache).
    [Fact]
    public Task ServerPwa_InstallableWithOfflineFallback() => RunAsync(async () =>
    {
        await Page.GotoAsync("/");

        // The manifest link is server-emitted into <head>, and the endpoint serves the right type with
        // a base-rooted start_url.
        Assert.Equal("/rask/manifest.webmanifest",
            await Page.Locator("link[rel=manifest]").GetAttributeAsync("href"));
        var manifestResponse = await Page.APIRequest.GetAsync(BaseUrl + "/rask/manifest.webmanifest");
        Assert.Contains("application/manifest+json", manifestResponse.Headers["content-type"]);
        var manifest = (await manifestResponse.JsonAsync())!.Value;
        Assert.Equal("/", manifest.GetProperty("start_url").GetString());

        // AddRaskPwa auto-registers the service worker; wait until it controls the page.
        await Page.WaitForFunctionAsync(
            "() => navigator.serviceWorker && navigator.serviceWorker.controller !== null",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });

        // The Server PWA showcase page renders its interactive demo.
        await Page.GotoAsync("/server-pwa");
        await Page.Locator("#pwa-notify").WaitForAsync();

        // Critical: offline navigation → static offline page (never a dead cached live shell).
        await Page.Context.SetOfflineAsync(true);
        try
        {
            await Page.GotoAsync("/about", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
            Assert.Contains("You're offline", await Page.ContentAsync());
        }
        finally
        {
            await Page.Context.SetOfflineAsync(false);
        }
    });
}
