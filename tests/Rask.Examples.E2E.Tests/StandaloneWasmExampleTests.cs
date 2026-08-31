using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     The standalone <c>Rask.Example.Wasm</c> example, published and served from a plain static-file
///     host (<see cref="StandaloneWasmAppFixture" />) — the "any static host" / GitHub Pages scenario,
///     with no Rask runtime in front of it. The journey reaches every page by loading
///     <c>/index.html</c> once and clicking the sidebar; deep-link, slow-3G, and WebSocket reconnect
///     steps are off (a static host just serves the published bundle — there's no live WS). The
///     shell-reload step stands in for the deep-route refresh, and the live-ticker sidebar nav inside
///     the journey covers the publish-render history regression specific to WasmLiveSession's
///     coalescing path.
/// </summary>
[Collection(StandaloneWasmExampleCollection.Name)]
public sealed class StandaloneWasmExampleTests : SharedSmokeTests
{
    // path → sidebar button label table. Only "/" is consulted by the journey (every other page is
    // reached by clicking the sidebar directly); the entry documents what the standalone host reaches.
    private static readonly Dictionary<string, string> SidebarLabels = new()
    {
        ["/"] = "All guides",
    };

    private readonly StandaloneWasmAppFixture _app;
    private bool _shellLoaded;

    public StandaloneWasmExampleTests(StandaloneWasmAppFixture app, PlaywrightFixture pw) : base(pw) => _app = app;

    protected override string BaseUrl => _app.BaseUrl;
    protected override string FixtureName => "StandaloneWasm";
    protected override string ServerLog => _app.ServerLog;

    // The first NavigateToAsync loads /index.html and waits for the home hero. The journey only ever
    // navigates to "/" through here; all other page transitions go through ClickSidebar (in-page SPA
    // navigation).
    protected override async Task NavigateToAsync(string path)
    {
        if (!SidebarLabels.TryGetValue(path, out var label))
        {
            throw new InvalidOperationException(
                $"StandaloneWasmExampleTests cannot navigate to '{path}' — no sidebar entry.");
        }

        if (!_shellLoaded)
        {
            await Page.GotoAsync("/index.html");
            await Expect(Page.Locator("main h1"))
                .ToContainTextAsync("Guides", new LocatorAssertionsToContainTextOptions { Timeout = 60_000 });
            _shellLoaded = true;
        }

        if (path == "/")
        {
            return;
        }

        await ClickSidebar(label);
        await Expect(Page).ToHaveURLAsync(new Regex($".*{Regex.Escape(path)}$"),
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });
    }

    [Fact]
    public Task Journey_WalksEveryPageAndUnusualActivity() => RunAsync(() =>
        RunShowcaseJourneyAsync(new ShowcaseJourneyOptions
        {
            DeepLink = false,
            OfflineReconnect = false,
            Slow3g = false,
            ReloadShellBoots = true,
        }));
}
