using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's Layout and Mockup components, in a browser.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitLayoutTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task EveryLayoutComponentRendersWithARealSize() => RunAsync(async () =>
    {
        await OpenAsync();

        foreach (var id in new[]
                 {
                     "ui-drawer", "ui-layout-rest", "ui-layout-mask", "ui-mockups",
                 })
        {
            var node = Page.Locator($"[data-testid='{id}']");
            await Expect(node).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            var box = await node.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.Height > 0, $"{id} rendered with zero height.");
        }
    });

    [Fact]
    public Task TheDrawerOpensAndThePageIsToldAboutIt() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-drawer']");
        var state = Page.Locator("[data-testid='ui-drawer-state']");

        await Expect(state).ToContainTextAsync("closed");

        await scope.GetByText("Open the drawer").ClickAsync();

        // Both halves. The panel slides because the checkbox is checked — that is daisyUI's CSS, not
        // the kit's — and the PAGE knows, which is what the checkbox alone could never report.
        await Expect(state).ToContainTextAsync("open");
        await Expect(scope.GetByText("Queues")).ToBeVisibleAsync();
    });

    [Fact]
    public Task ACheckedControlIsActuallyCheckedInTheDom() => RunAsync(async () =>
    {
        // The regression this guards is the one markup could not show: the kit wrote the checked state
        // into the VALUE attribute, so a control the page said was on arrived off. Asserted through the
        // DOM's own property rather than the attribute, which is what a reader actually sees.
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await ClickSidebar("Data input");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Data input",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        var remember = Page.Locator("[data-testid='ui-choices'] input.checkbox").First;
        await Expect(remember).ToBeCheckedAsync();

        var alerts = Page.Locator("[data-testid='ui-choices'] input.toggle").First;
        await Expect(alerts).Not.ToBeCheckedAsync();
    });

    [Fact]
    public Task TheCodeMockupShowsItsPrefixesAndItsText() => RunAsync(async () =>
    {
        await OpenAsync();

        var code = Page.Locator("[data-testid='ui-mockups'] .mockup-code");

        await Expect(code).ToBeVisibleAsync();
        await Expect(code).ToContainTextAsync("rask new shop");
        await Expect(code.Locator("pre[data-prefix='$']")).ToHaveCountAsync(2);
    });

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Layout & mockups");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Layout",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }
}
