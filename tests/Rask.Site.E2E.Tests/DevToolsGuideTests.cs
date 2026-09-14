using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The DevTools guide, in a browser: the one guide that shows pictures, so the one place a picture that the site does
///     not actually serve would show up as a broken image rather than as a green unit test about its path.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class DevToolsGuideTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task EveryScreenshotInTheGuideLoads() => RunAsync(async () =>
    {
        await Page.GotoAsync(Docs + "/guides/devtools");
        await Expect(Page.Locator("main .markdown-body h1").First).ToContainTextAsync("DevTools",
            new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        await AssertNoGlobalCrashAsync();

        var images = Page.Locator("main .markdown-body img");
        Assert.Equal(4, await images.CountAsync());

        for (var i = 0; i < 4; i++)
        {
            var image = images.Nth(i);
            // Lazy, so each is scrolled to before it is asked whether it loaded.
            await image.ScrollIntoViewIfNeededAsync();
            var src = await image.GetAttributeAsync("src");
            await Expect(image).ToHaveJSPropertyAsync("complete", true,
                new LocatorAssertionsToHaveJSPropertyOptions { Timeout = 15_000 });
            Assert.True(
                await image.EvaluateAsync<int>("img => img.naturalWidth") > 0,
                $"the guide's picture {src} did not load");
            Assert.False(string.IsNullOrWhiteSpace(await image.GetAttributeAsync("alt")), $"{src} has no alt text");
        }
    });
}
