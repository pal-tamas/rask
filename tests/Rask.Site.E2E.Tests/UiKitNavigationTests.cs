using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's Navigation components, in a browser — where the native popover actually opens.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitNavigationTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task EveryNavigationComponentRendersWithARealSize() => RunAsync(async () =>
    {
        await OpenAsync();

        foreach (var id in new[] { "ui-megamenu", "ui-tabs", "ui-nav-rest" })
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
    public Task TheMegamenuOpensAPanelThroughTheNativePopover() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-megamenu']");
        var panel = Page.Locator("#mm-products");

        await Expect(panel).ToBeHiddenAsync();

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Products" }).ClickAsync();

        // No class was written and no C# state changed — the browser put it in the top layer because
        // the button names it with popovertarget. This is the assertion that proves the mechanism.
        await Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // And its contents are reachable, not merely present: a panel in the top layer with its links
        // still inert would satisfy the visibility check above and be useless.
        await Expect(panel).ToContainTextAsync("Scaffolding and deploys.");
        await Expect(panel.GetByRole(AriaRole.Link)).ToHaveCountAsync(4);
    });

    [Fact]
    public Task EscapeClosesTheMegamenuBecauseTheBrowserOwnsIt() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-megamenu']");
        var panel = Page.Locator("#mm-products");

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Products" }).ClickAsync();
        await Expect(panel).ToBeVisibleAsync();

        // Escape, light-dismiss and the top layer all come free with [popover]. Nothing in the kit
        // implements this, which is the point of using it.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(panel).ToBeHiddenAsync();
    });

    [Fact]
    public Task OpeningOnePanelClosesTheOther() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-megamenu']");

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Products" }).ClickAsync();
        await Expect(Page.Locator("#mm-products")).ToBeVisibleAsync();

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Company" }).ClickAsync();

        // popover="auto" light-dismisses its siblings. Two open panels would be the sign the panels
        // were not siblings of one another, which is also what breaks daisyUI's nth-of-type anchoring.
        await Expect(Page.Locator("#mm-company")).ToBeVisibleAsync();
        await Expect(Page.Locator("#mm-products")).ToBeHiddenAsync();
    });

    [Fact]
    public Task EveryTabIsARealLink() => RunAsync(async () =>
    {
        await OpenAsync();

        var tabs = Page.Locator("[data-testid='ui-tabs'] [role='tablist']").First;

        await Expect(tabs).ToBeVisibleAsync();
        await Expect(tabs.Locator("a.tab")).ToHaveCountAsync(3);

        // The active one is marked for CSS and for assistive technology, and it is an anchor — which is
        // what makes it bookmarkable and back-button-able.
        var active = tabs.Locator("a.tab-active");
        await Expect(active).ToHaveCountAsync(1);
        await Expect(active).ToHaveAttributeAsync("aria-selected", "true");
        Assert.NotNull(await active.GetAttributeAsync("href"));
    });

    [Fact]
    public Task PagesWithAnAddressAreLinksAndTheCurrentPageIsNot() => RunAsync(async () =>
    {
        await OpenAsync();

        var pager = Page.Locator("[data-testid='ui-pagination-links']");

        // Four pages, the first current: three links and one marker that is not a link.
        await Expect(pager.Locator("a.join-item")).ToHaveCountAsync(3);
        await Expect(pager.Locator("[aria-current='page']")).ToHaveTextAsync("1");
        await Expect(pager.Locator("button")).ToHaveCountAsync(0);

        var href = await pager.Locator("a.join-item").First.GetAttributeAsync("href") ?? "";
        Assert.EndsWith("/ui/navigation/?page=2", href, StringComparison.Ordinal);
    });

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Navigation");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Navigation",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }
}
