using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's Navigation components, in a browser — where a link is actually a link.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitNavigationTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task Every_navigation_component_renders_with_a_real_size() => RunAsync(async () =>
    {
        await OpenAsync();

        foreach (var id in new[] { "ui-tabs", "ui-nav-rest" })
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
    public Task Every_tab_is_a_real_link() => RunAsync(async () =>
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
    public Task Pages_with_an_address_are_links_and_the_current_page_is_not() => RunAsync(async () =>
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
        await Expect(Page.Locator(".side-nav a.side-nav-link[aria-current='page']").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Navigation");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Navigation",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }
}
