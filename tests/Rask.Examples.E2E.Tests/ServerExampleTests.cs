using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

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
            SignalingRelay = true,
        }));

    // The seam a browser takeover rests on. Both hosts splice the same delegated-event listeners, so a
    // browser runtime booting while the server one is still live would answer every click twice — once
    // over the socket and once in WebAssembly. __raskHandOff is how the server runtime stands down.
    [Fact]
    public Task HandOff_StopsTheServerRuntimeDrivingThePage() => RunAsync(async () =>
    {
        // Counted rather than inspected: rask.js keeps its socket in a closure, so the only honest way
        // to prove it did not come back is to watch the browser open one. A leaked handover shows up
        // here as a second socket, which is precisely what the reconnect ladder would do.
        var sockets = 0;
        Page.WebSocket += (_, _) => Interlocked.Increment(ref sockets);

        await Page.GotoAsync("/");
        await Expect(Page.Locator("[data-rask-root]")).ToHaveCountAsync(1);
        await Expect(Page.Locator("[data-show]")).ToHaveCountAsync(0);

        Assert.Equal("server", await Page.EvaluateAsync<string>("() => window.__raskOwner"));
        var beforeHandOff = Volatile.Read(ref sockets);

        await Page.EvaluateAsync("() => window.__raskHandOff()");

        Assert.Equal("wasm", await Page.EvaluateAsync<string>("() => window.__raskOwner"));

        // Past the reconnect ladder's first rung with margin. If the handover leaked into
        // scheduleReconnect, the browser has opened another socket by now.
        await Task.Delay(2500);
        Assert.Equal(beforeHandOff, Volatile.Read(ref sockets));

        // And the overlay stays down: this is not a disconnection the user has to see. The element is
        // always in the DOM and toggled by data-show, so presence proves nothing — absence of the
        // attribute is the assertion.
        await Expect(Page.Locator("[data-show]")).ToHaveCountAsync(0);
    });

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

    // The redeploy form restore (#571). When a replacement server can't carry a session over, the page
    // reloads and rask.js re-applies the fields the user had actually edited — but only when the
    // replacement rendered the same base the old server did.
    //
    // This drives the APPLY half against a real reload, because that is where every risk sits: the
    // merge decision, arming the value guard against the first pristine frame, and pushing the restored
    // value back so the SERVER MODEL converges. The last of those is the whole point of the feature and
    // the only thing that can't be faked — a form showing values the server doesn't have is exactly the
    // data loss that kept field restore out of the first cut.
    //
    // The snapshot is written by hand rather than by forcing a real redeploy: saveRestoreFields runs
    // inside the client IIFE on a shutdown the harness has no way to stage. Its own half is covered by
    // RestoreFieldBaseTests (the dirty base capture) and ShutdownClientContractTests (the shape).
    [Fact]
    public Task RedeployReload_RestoresEditedFields_AndConvergesTheServerModel() => RunAsync(async () =>
    {
        await Page.GotoAsync("/guides/forms");
        // /guides/forms co-mounts many demos; wait for a late one so nothing races hydration.
        await Expect(Page.Locator("#fc-input-bound")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        var bound = Page.Locator("#fc-input-bound");
        var echo = Page.Locator("#fc-input-bound-out");

        // Type, and confirm the server really is holding it — the echo renders the bound model.
        await bound.FillAsync("survives-the-redeploy");
        await Expect(echo).ToContainTextAsync("survives-the-redeploy",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // The reload lands on a pristine session, so the replacement renders value="" — the same base
        // the old server had rendered when the field was first touched. Bases match, edit is newest.
        await WriteRestoreFieldsAsync("/guides/forms",
            """[{"k":"fc-input-bound","t":"text","b":"","v":"survives-the-redeploy"}]""");
        await Page.ReloadAsync();

        await Expect(Page.Locator("#fc-input-bound")).ToHaveValueAsync("survives-the-redeploy",
            new LocatorAssertionsToHaveValueOptions { Timeout = 20_000 });
        // ...and it STAYS: the first frame the server sends is computed from its own pristine model,
        // so without the armed value guard the text would be wiped and only flicker back afterwards.
        await Task.Delay(2_000);
        await Expect(Page.Locator("#fc-input-bound")).ToHaveValueAsync("survives-the-redeploy");
        // The assertion that matters. The echo renders the SERVER's model, so this is only green if the
        // converge message landed and the server holds what the page is showing.
        await Expect(Page.Locator("#fc-input-bound-out")).ToContainTextAsync("survives-the-redeploy",
            new LocatorAssertionsToContainTextOptions { Timeout = 20_000 });

        // Consume-once: the snapshot must not survive its own reload and re-apply later.
        await Page.ReloadAsync();
        await Expect(Page.Locator("#fc-input-bound")).ToHaveValueAsync("",
            new LocatorAssertionsToHaveValueOptions { Timeout = 20_000 });

        // A base the replacement doesn't match means the new server knows something this stale copy
        // doesn't — it wins, and the edit is dropped silently.
        await WriteRestoreFieldsAsync("/guides/forms",
            """[{"k":"fc-input-bound","t":"text","b":"a-base-the-server-never-rendered","v":"should-be-dropped"}]""");
        await Page.ReloadAsync();
        await Expect(Page.Locator("#fc-input-bound")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });
        await Task.Delay(2_000);
        await Expect(Page.Locator("#fc-input-bound")).ToHaveValueAsync("");
        await Expect(Page.Locator("#fc-input-bound-out")).Not.ToContainTextAsync("should-be-dropped");
    });

    // Secrets are excluded unconditionally, and the exclusion is re-checked on the apply side — so even
    // a hand-planted snapshot naming a password field is refused rather than trusted.
    [Fact]
    public Task RedeployReload_NeverRestoresAPasswordField() => RunAsync(async () =>
    {
        await Page.GotoAsync("/guides/forms-validation");
        await Expect(Page.Locator("#v4-password")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        await WriteRestoreFieldsAsync("/guides/forms-validation",
            """[{"k":"v4-password","t":"password","b":"","v":"hunter2"}]""");
        await Page.ReloadAsync();

        await Expect(Page.Locator("#v4-password")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });
        await Task.Delay(2_000);
        await Expect(Page.Locator("#v4-password")).ToHaveValueAsync("");
    });

    // Plant a snapshot in the shape rask.js's saveRestoreFields writes, under its own key.
    private Task WriteRestoreFieldsAsync(string path, string fieldsJson) =>
        Page.EvaluateAsync(
            "([p, f]) => sessionStorage.setItem('rask:restore:fields', JSON.stringify({p, f: JSON.parse(f)}))",
            new[] { path, fieldsJson });
}
