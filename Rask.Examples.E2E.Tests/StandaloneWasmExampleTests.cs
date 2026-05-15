using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     Smoke tests for the standalone <c>Rask.Example.Wasm</c> example, served by
///     WasmAppHost (the dev launcher used by <c>dotnet run</c>). WasmAppHost has no
///     SPA fallback for unknown paths, so the inherited <c>SharedSmokeTests</c>
///     forms suite is reached by overriding <see cref="NavigateToAsync"/> to load
///     <c>/index.html</c> once and click the sidebar entry for the requested route.
///     Any test in this class itself (i.e. not inherited) targets behaviour that's
///     specific to the WasmAppHost launcher.
/// </summary>
[Collection(StandaloneWasmExampleCollection.Name)]
public sealed class StandaloneWasmExampleTests : SharedSmokeTests
{
    // path → sidebar button label table. The labels come straight from ShowcaseLayout.Links.
    // A route not present here means the standalone collection cannot reach it — adding it
    // to the sidebar is the right fix, not a NavigateToAsync workaround.
    private static readonly Dictionary<string, string> SidebarLabels = new()
    {
        ["/"] = "Welcome",
        ["/tags"] = "Tag factories",
        ["/primitives"] = "Primitives",
        ["/props"] = "Universal props",
        ["/components"] = "User components",
        ["/routing"] = "Routing",
        ["/users/42"] = "Route + query params",
        ["/navigator"] = "Navigator",
        ["/lifecycle"] = "Lifecycle",
        ["/cancellation"] = "Cancellation",
        ["/disposal"] = "Disposal",
        ["/events"] = "Events",
        ["/virtualize"] = "Virtualize",
        ["/boom"] = "Error boundary",
        ["/binding"] = "Two-way binding",
        ["/validation"] = "Validation",
        ["/nested-forms"] = "Complex models",
        ["/scoped-css"] = "Scoped CSS",
        ["/view-transitions"] = "View transitions",
        ["/http"] = "HttpClient + DI",
    };

    private readonly StandaloneWasmAppFixture _app;
    private bool _shellLoaded;

    public StandaloneWasmExampleTests(StandaloneWasmAppFixture app, PlaywrightFixture pw) : base(pw) => _app = app;

    protected override string BaseUrl => _app.BaseUrl;
    protected override string FixtureName => "StandaloneWasm";
    protected override string ServerLog => _app.ServerLog;

    // The first NavigateToAsync of a test loads /index.html and waits for the home hero
    // (the only real GET WasmAppHost responds to). Subsequent navigations stay inside
    // the SPA by clicking the sidebar entry — same in-page transitions a user would do.
    protected override async Task NavigateToAsync(string path)
    {
        if (!SidebarLabels.TryGetValue(path, out var label))
        {
            throw new InvalidOperationException(
                $"StandaloneWasmExampleTests cannot navigate to '{path}' — no sidebar entry. " +
                "Add it to ShowcaseLayout.Links + this map, or limit the test to ExampleSmokeTests.");
        }

        if (!_shellLoaded)
        {
            await Page.GotoAsync("/index.html");
            await Expect(Page.Locator("h1.display-5"))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 60_000 });
            _shellLoaded = true;
        }

        if (path == "/")
        {
            // Already on Welcome after the shell load — no extra click required, and the
            // active sidebar button is harmless to re-click but adds latency on cold WASM.
            return;
        }

        await ClickSidebar(label);
        await Expect(Page).ToHaveURLAsync(new Regex($".*{Regex.Escape(path)}$"),
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });
    }

    // ---------- Host-specific smokes (kept on top of the inherited shared suite) ----------

    [Fact]
    public async Task PageReload_AtIndex_StillBoots()
    {
        // WasmAppHost serves index.html with no caching guarantees beyond what the SDK
        // produces. A reload at the shell URL must always boot the runtime cleanly.
        try
        {
            await NavigateToAsync("/");
            await Page.ReloadAsync();
            await Expect(Page.Locator("h1.display-5"))
                .ToContainTextAsync("The Rask framework",
                    new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        }
        finally
        {
            await TestArtifacts.DumpAsync(Page, FixtureName, nameof(PageReload_AtIndex_StillBoots), ServerLog);
        }
    }
}
