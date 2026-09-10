using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

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
            // False now, and this is a real change in what the site can do rather than a test
            // concession. The relay is ASP.NET-side (Rask.Signaling), and this app used to be booted
            // behind Rask.Example.Wasm.Host — an ASP.NET host that mapped it. There is one app now and
            // GitHub Pages serves it as static files, so nothing maps the relay in production either.
            // The demo says so in its own UI on a host without one.
            SignalingRelay = false,
        }));

    // The WASM-only PWA example page (PwaDemo) lives in the WASM host and is surfaced in the shared
    // sidebar via a host-contributed ShowcaseNavEntry. Verify the entry routes and the page renders.
    [Fact]
    public Task PwaExample_RoutesAndRenders() => RunAsync(async () =>
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await OpenSection("notifications", "Notifications, push & badge");
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
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await OpenSection("wake-lock", "Wake lock");
        await Expect(Page.Locator("#wakelock-toggle")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    });

    // The WASM-only Screen Orientation page (OrientationDemo) — verify it routes, renders, and that
    // reading the orientation updates the status (screen.orientation is available in headless Chromium).
    [Fact]
    public Task OrientationExample_RoutesAndReads() => RunAsync(async () =>
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await OpenSection("orientation", "Orientation");

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
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await OpenSection("fullscreen", "Fullscreen");
        await Expect(Page.Locator("#fullscreen-enter")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // CodeSample shows the demo's real source beside the live result.
        await Expect(Page.Locator("[data-section=fullscreen] .sample-code").First).ToContainTextAsync("IFullscreen",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    // The WASM-only Install prompt page (InstallPromptDemo) — verify it routes and renders. The browser
    // won't fire beforeinstallprompt headlessly, so the status reports "not installable yet"; real install
    // needs a user gesture + install criteria, covered by unit tests.
    [Fact]
    public Task InstallPromptExample_RoutesAndRenders() => RunAsync(async () =>
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await OpenSection("install", "Install prompt");
        await Expect(Page.Locator("#install-status")).ToContainTextAsync("not installable yet",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        // CodeSample shows the demo's real source beside the live result.
        await Expect(Page.Locator("[data-section=install] .sample-code").First).ToContainTextAsync("IInstallPrompt",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    // The WASM-only Picture-in-Picture page (PictureInPictureDemo) — verify it routes and renders. The
    // sibling scoped JS synthesizes a canvas-stream video; entering the real miniplayer needs a gesture and
    // is unreliable headlessly, so this only checks the UI + CodeSample source.
    [Fact]
    public Task PictureInPictureExample_RoutesAndRenders() => RunAsync(async () =>
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await OpenSection("picture-in-picture", "Picture-in-Picture");
        await Expect(Page.Locator("#pip-enter")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator("[data-section=picture-in-picture] .sample-code").First).ToContainTextAsync("IPictureInPicture",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    // The WASM-only EyeDropper page (EyeDropperDemo) — verify it routes and renders. open() needs a gesture
    // and the picker can't be driven headlessly, so this only checks the UI + CodeSample source.
    [Fact]
    public Task EyeDropperExample_RoutesAndRenders() => RunAsync(async () =>
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await OpenSection("eye-dropper", "EyeDropper");
        await Expect(Page.Locator("#eyedropper-pick")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator("[data-section=eye-dropper] .sample-code").First).ToContainTextAsync("IEyeDropper",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    // The WASM-only Idle detection page (IdleDetectorDemo) — verify it routes and renders. The permission
    // needs a gesture and idle can't be simulated headlessly, so this only checks the UI + CodeSample source.
    [Fact]
    public Task IdleDetectionExample_RoutesAndRenders() => RunAsync(async () =>
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await OpenSection("idle", "Idle detection");
        await Expect(Page.Locator("#idle-start")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator("[data-section=idle] .sample-code").First).ToContainTextAsync("IIdleDetector",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    // The WASM-only Camera & microphone page (MediaDevicesDemo) — verify it routes and renders. getUserMedia
    // opens a real capture permission prompt that can't be driven without fake-media flags, so this only
    // checks the UI + CodeSample source; the call shapes are covered by unit tests.
    [Fact]
    public Task MediaDevicesExample_RoutesAndRenders() => RunAsync(async () =>
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await OpenSection("media-devices", "Camera & microphone");
        await Expect(Page.Locator("#media-start")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(Page.Locator("[data-section=media-devices] .sample-code").First).ToContainTextAsync("IMediaDevices",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });


    /// <summary>
    ///     Opens the consolidated PWA page and scrolls to one demo's section.
    /// </summary>
    /// <remarks>
    ///     These thirteen demos were thirteen sidebar rows and thirteen pages, each asserted through its own
    ///     <c>main h1</c>. They are sections of one page now, so the row is the same for all of them and the
    ///     heading to assert is the section's <c>h2</c> — anchored by id, which is also what the page's own
    ///     rail links to, so a section that lost its anchor fails here rather than leaving a rail link that
    ///     quietly scrolls nowhere.
    /// </remarks>
    private async Task OpenSection(string slug, string title)
    {
        await ClickSidebar("PWA & device APIs");

        await Expect(Page.Locator("main h1")).ToContainTextAsync("PWA & device APIs",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        await Expect(Page.Locator($"main h2#{slug}")).ToContainTextAsync(title,
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }

}
