using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's Feedback components, in a browser.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitFeedbackTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task EveryFeedbackComponentRendersWithARealSize() => RunAsync(async () =>
    {
        await OpenAsync();

        foreach (var id in new[]
                 {
                     "ui-alert", "ui-loading", "ui-progress", "ui-tooltip", "ui-skeleton", "ui-toast",
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
    public Task EveryLoadingShapeIsDrawnRatherThanBeingAnEmptySpan() => RunAsync(async () =>
    {
        await OpenAsync();

        // `loading` on its own is an unstyled span. Six shapes, six non-zero boxes: this is the check
        // that a shape class reached the compiled sheet at all.
        var indicators = Page.Locator("[data-testid='ui-loading'] .loading");
        await Expect(indicators).ToHaveCountAsync(6);

        for (var i = 0; i < 6; i++)
        {
            var box = await indicators.Nth(i).BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.Width > 0 && box.Height > 0, $"loading indicator {i} drew nothing.");
        }
    });

    [Fact]
    public Task TheProgressElementReportsItsOwnValue() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-progress']");
        var bar = scope.Locator("progress").First;

        await Expect(bar).ToHaveAttributeAsync("value", "62");

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "+10" }).ClickAsync();

        // A real <progress>, so the value is the element's own rather than a width the kit painted.
        await Expect(bar).ToHaveAttributeAsync("value", "72");
    });

    [Fact]
    public Task AToastAppearsOnDemandAndCanBeDismissed() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-toast']");

        // Scoped to the toast's own section, not the page. UiAlert renders role="status" for any tone
        // that is not an error — which is right, an outcome should be announced politely rather than
        // interrupting — and this page has an alert reading "Saved." as well, so a page-wide locator
        // matched two elements and went on matching the alert after the toast was dismissed.
        var toast = scope.Locator("[role='status']");

        // No assertion that it starts absent, deliberately. The harness re-runs this body on a boot
        // failure (RaceAgainstBootFailureAsync), and the second attempt gets a page the first one had
        // already clicked Save on — so "there is no toast yet" is a claim about the harness rather than
        // about the component. What the component owes is the TRANSITION, which is what is asserted.
        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();
        await Expect(toast).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        await toast.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Dismiss" })
            .ClickAsync();
        await Expect(toast).ToHaveCountAsync(0);
    });

    [Fact]
    public Task AnOpenTooltipIsVisibleWithoutAHover() => RunAsync(async () =>
    {
        await OpenAsync();

        // The only way a touch user ever sees one. Asserted through the element daisyUI draws the
        // bubble with, since the tip lives in an attribute rather than in text.
        var open = Page.Locator("[data-testid='ui-tooltip'] .tooltip-open");

        await Expect(open).ToHaveCountAsync(1);
        await Expect(open).ToHaveAttributeAsync("data-tip", "Always shown");
    });

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Feedback");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Feedback",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }
}
