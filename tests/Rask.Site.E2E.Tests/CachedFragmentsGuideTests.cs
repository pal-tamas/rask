using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The cached-fragments guide's live demo, in the browser app (#1323): a report behind <c>Cached</c> shown again is
///     handed the markup the tab kept — never drawn twice — and drawn afresh once it is forgotten. The WebAssembly half
///     of the feature, at a desktop and a phone width.
/// </summary>
public sealed class CachedFragmentsGuideTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public Task A_report_shown_again_is_the_kept_markup_until_it_is_forgotten(int width, int height) => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync(Docs + "/guides/cached-fragments");
        await Expect(Page.Locator("main .markdown-body h1").First).ToContainTextAsync("Cached fragments",
            new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        var caption = Page.Locator("#cached-report caption");
        var toggle = Page.Locator("#cached-toggle");
        await Expect(toggle).ToHaveTextAsync("Hide the report", new LocatorAssertionsToHaveTextOptions { Timeout = 45_000 });
        await Expect(caption).ToHaveTextAsync("Report, draw #1");

        await toggle.ClickAsync();
        await Expect(toggle).ToHaveTextAsync("Show the report");
        await toggle.ClickAsync();
        await Expect(caption).ToHaveTextAsync("Report, draw #1");

        await Page.Locator("#cached-forget").ClickAsync();
        await toggle.ClickAsync();
        await Expect(toggle).ToHaveTextAsync("Show the report");
        await toggle.ClickAsync();
        await Expect(caption).ToHaveTextAsync("Report, draw #2");
        await Expect(Page.Locator("#cached-report tbody tr")).ToHaveCountAsync(3);
        await AssertNoGlobalCrashAsync();
    });
}
