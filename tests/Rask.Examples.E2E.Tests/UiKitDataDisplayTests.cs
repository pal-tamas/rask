using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     The kit's Data display components, in a browser — the size check no markup assertion can make,
///     and the state changes no static render can show.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitDataDisplayTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task EveryDataDisplayComponentRendersWithARealSize() => RunAsync(async () =>
    {
        await OpenAsync();

        foreach (var id in new[]
                 {
                     "ui-accordion", "ui-collapse", "ui-aura", "ui-text-rotate", "ui-hover-3d",
                     "ui-hover-gallery", "ui-display-rest",
                 })
        {
            var node = Page.Locator($"[data-testid='{id}']");
            await Expect(node).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            var box = await node.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.Height > 0, $"{id} rendered with zero height, which is what an unstyled kit component looks like.");
        }
    });

    [Fact]
    public Task TheAccordionOpensOneSectionAtATime() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-accordion']");
        var state = Page.Locator("[data-testid='ui-accordion-state']");

        await Expect(state).ToContainTextAsync("ship");
        await Expect(scope.GetByText("Ships within two working days")).ToBeVisibleAsync();

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Payment" }).ClickAsync();

        // The page names the open section, which is exactly what a run of <details name> could not do:
        // the browser closed the others without telling anyone which one won.
        await Expect(state).ToContainTextAsync("pay");
        await Expect(scope.GetByText("Card or bank transfer")).ToBeVisibleAsync();
        await Expect(scope.GetByText("Ships within two working days")).ToBeHiddenAsync();
    });

    [Fact]
    public Task PressingTheOpenSectionClosesEverything() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-accordion']");
        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Shipping" }).ClickAsync();

        await Expect(Page.Locator("[data-testid='ui-accordion-state']")).ToContainTextAsync("All sections closed");
    });

    [Fact]
    public Task TheCollapseOpensAndClosesFromCSharpState() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-collapse']");
        var body = scope.GetByText("Nothing in here is required.");

        await Expect(body).ToBeHiddenAsync();
        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Advanced settings" })
            .ClickAsync();
        await Expect(body).ToBeVisibleAsync();
    });

    [Fact]
    public Task TheRotatorShowsEveryWordInTheMarkup() => RunAsync(async () =>
    {
        await OpenAsync();

        // The animation is CSS, so all four words are in the DOM whatever the browser is doing with
        // them. A reader who never sees the animation still reads the list.
        var scope = Page.Locator("[data-testid='ui-text-rotate']");

        await Expect(scope).ToContainTextAsync("fast");
        await Expect(scope).ToContainTextAsync("typed");
        await Expect(scope).ToContainTextAsync("small");
        await Expect(scope).ToContainTextAsync("whole");
    });

    [Fact]
    public Task TheDecorativeComponentsAnnounceNothing() => RunAsync(async () =>
    {
        await OpenAsync();

        // An aura that claimed a role would put a meaningless announcement between a screen-reader user
        // and the content it is drawn around.
        var aura = Page.Locator("[data-testid='ui-aura'] .aura").First;

        await Expect(aura).ToBeVisibleAsync();
        Assert.Null(await aura.GetAttributeAsync("role"));
        Assert.Null(await aura.GetAttributeAsync("aria-label"));
    });

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Data display");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Data display",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }
}
