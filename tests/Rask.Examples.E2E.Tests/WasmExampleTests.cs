using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// The Wasm.Host journey: the WASM bundle served by an ASP.NET host with a SPA fallback, so
// deep-link + refresh and slow-3G apply. There is no server WebSocket to drop, so the
// offline→reconnect step is off.
[Collection(WasmExampleCollection.Name)]
public sealed class WasmExampleTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task Journey_WalksEveryPageAndUnusualActivity() => RunAsync(() =>
        RunShowcaseJourneyAsync(new ShowcaseJourneyOptions
        {
            DeepLink = true,
            OfflineReconnect = false,
            Slow3g = true,
            SignalingRelay = true,
        }));

    // The WASM-only PWA example page (PwaDemo) lives in the WASM host and is surfaced in the shared
    // sidebar via a host-contributed ShowcaseNavEntry. Verify the entry routes and the page renders.
    [Fact]
    public Task PwaExample_RoutesAndRenders() => RunAsync(async () =>
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("PWA demo");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("PWA — notifications & push",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator("#pwa-notify")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#pwa-push")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // The "send a test push" button (drives the Rask.WebPush backend) renders, disabled until a
        // subscription exists. Real delivery needs a push service, so we only assert the UI here.
        await Expect(Page.Locator("#pwa-push-send")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // The badge section added alongside notifications/push.
        await Expect(Page.Locator("#pwa-badge-inc")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    });

    // The WASM-only Wake Lock page (WakeLockDemo) — verify the host-contributed sidebar entry routes
    // and the page renders. The lock itself can't be asserted headlessly, so we only check the UI.
    [Fact]
    public Task WakeLockExample_RoutesAndRenders() => RunAsync(async () =>
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Wake lock");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Wake lock",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator("#wakelock-toggle")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    });

    // The WASM-only Screen Orientation page (OrientationDemo) — verify it routes, renders, and that
    // reading the orientation updates the status (screen.orientation is available in headless Chromium).
    [Fact]
    public Task OrientationExample_RoutesAndReads() => RunAsync(async () =>
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Orientation");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Orientation",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        await Page.Locator("#orientation-read").ClickAsync();
        // Assert the OUTCOME, not the absence of the placeholder.
        //
        // This used to be Not.ToContainText("read to see"), which passes on anything that is not the
        // idle text — including the demo's own "read failed". So the moment the demo started reporting
        // a thrown read (#810), a negative assertion would have started passing on the failure it
        // exists to catch. Same shape as the substring trap where "connected" matches "disconnected":
        // a negative assertion is satisfied by outcomes nobody enumerated.
        //
        // Matching the two legitimate shapes instead — "<type> (<angle>°)" or "not supported" — means a
        // failure prints what the element actually said, so "read failed" and a click that never landed
        // stop producing the same red. That distinction is the whole point of the issue.
        // The type is the OrientationType enum's name — "LandscapePrimary", not the web platform's
        // "landscape-primary". The first version of this regex assumed the latter and failed against
        // 'LandscapePrimary (0°)', which is the assertion earning its keep on its first run: the old
        // Not.ToContainText("read to see") would have passed on that too, and on anything else.
        await Expect(Page.Locator("#orientation-current")).ToHaveTextAsync(
            new Regex(@"^(?:[A-Za-z][A-Za-z-]* \(-?\d+°\)|not supported)$"),
            new LocatorAssertionsToHaveTextOptions { Timeout = 10_000 });
    });

    // The WASM-only Fullscreen page (FullscreenDemo) — verify it routes and renders. Real fullscreen
    // needs a user gesture and is unreliable headlessly, so this only checks the UI + CodeSample source.
    [Fact]
    public Task FullscreenExample_RoutesAndRenders() => RunAsync(async () =>
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Fullscreen");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Fullscreen",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator("#fullscreen-enter")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // CodeSample shows the demo's real source beside the live result.
        await Expect(Page.Locator(".sample-code")).ToContainTextAsync("IFullscreen",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    // The WASM-only Install prompt page (InstallPromptDemo) — verify it routes and renders. The browser
    // won't fire beforeinstallprompt headlessly, so the status reports "not installable yet"; real install
    // needs a user gesture + install criteria, covered by unit tests.
    [Fact]
    public Task InstallPromptExample_RoutesAndRenders() => RunAsync(async () =>
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Install prompt");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Install prompt",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator("#install-status")).ToContainTextAsync("not installable yet",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        // CodeSample shows the demo's real source beside the live result.
        await Expect(Page.Locator(".sample-code")).ToContainTextAsync("IInstallPrompt",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    // The WASM-only Picture-in-Picture page (PictureInPictureDemo) — verify it routes and renders. The
    // sibling scoped JS synthesizes a canvas-stream video; entering the real miniplayer needs a gesture and
    // is unreliable headlessly, so this only checks the UI + CodeSample source.
    [Fact]
    public Task PictureInPictureExample_RoutesAndRenders() => RunAsync(async () =>
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Picture-in-Picture");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Picture-in-Picture",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator("#pip-enter")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator(".sample-code")).ToContainTextAsync("IPictureInPicture",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    // The WASM-only EyeDropper page (EyeDropperDemo) — verify it routes and renders. open() needs a gesture
    // and the picker can't be driven headlessly, so this only checks the UI + CodeSample source.
    [Fact]
    public Task EyeDropperExample_RoutesAndRenders() => RunAsync(async () =>
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("EyeDropper");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("EyeDropper",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator("#eyedropper-pick")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator(".sample-code")).ToContainTextAsync("IEyeDropper",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    // The WASM-only Idle detection page (IdleDetectorDemo) — verify it routes and renders. The permission
    // needs a gesture and idle can't be simulated headlessly, so this only checks the UI + CodeSample source.
    [Fact]
    public Task IdleDetectionExample_RoutesAndRenders() => RunAsync(async () =>
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Idle detection");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Idle detection",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator("#idle-start")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator(".sample-code")).ToContainTextAsync("IIdleDetector",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    // The WASM-only Camera & microphone page (MediaDevicesDemo) — verify it routes and renders. getUserMedia
    // opens a real capture permission prompt that can't be driven without fake-media flags, so this only
    // checks the UI + CodeSample source; the call shapes are covered by unit tests.
    [Fact]
    public Task MediaDevicesExample_RoutesAndRenders() => RunAsync(async () =>
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Camera & mic");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Camera & microphone",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator("#media-start")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator(".sample-code")).ToContainTextAsync("IMediaDevices",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    // A real Blazor component (samples/Rask.Example.Razor's PriceTicker.razor, compiled by the Razor
    // SDK in a referenced class library) hosted as a Rask island — on browser-WebAssembly.
    //
    // This test exists because of a failure with NO build warning, NO console error and NO exception:
    // the showcase publishes TRIMMED, and a hosted component's parameters are assigned by reflection
    // inside Microsoft.AspNetCore.Components, on a type Rask does not own. Without the
    // DynamicallyAccessedMembers annotation on BlazorComponent<TComponent> the trimmer removes those
    // property setters and the island renders EMPTY — <rask-blazor></rask-blazor> and nothing else.
    // So every assertion below is on the hosted component's own OUTPUT: an empty island is exactly
    // what a presence check would pass on.
    [Fact]
    public Task BlazorIslandExample_RendersHostedComponentAndRoundTripsItsOwnEvents() => RunAsync(async () =>
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Blazor island");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Blazor island",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        // The parameters crossed: Symbol and Price are C# values the island handed the hosted
        // component, and they only appear if the trimmer left its setters alone.
        await Expect(Page.Locator("[data-testid=ticker-symbol]")).ToHaveTextAsync("RASK",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator("[data-testid=ticker-price]")).ToContainTextAsync("12.50",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // The HOSTED component's own @onclick, with no Blazor circuit: Blazor mints the handler id
        // during the static render and the island writes Rask's data-rask-on-click in its place, so
        // the click travels the same channel every other event on this page uses.
        await Page.ClickAsync("[data-testid=ticker-watch]");
        await Expect(Page.Locator("[data-testid=ticker-watches]")).ToContainTextAsync("watching: 1",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // ...and its @bind, which travels the INPUT channel instead — the one that carries a value.
        await Page.FillAsync("[data-testid=ticker-note]", "live bind");
        await Expect(Page.Locator("[data-testid=ticker-note-echo]")).ToContainTextAsync("live bind",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // The page's Rask half is still Rask's: the island owns its own subtree and nothing more.
        await Expect(Page.Locator(".sample-code")).ToContainTextAsync("BlazorComponent",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });
}
