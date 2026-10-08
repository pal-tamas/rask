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
    public Task Every_navigation_component_renders_with_a_real_size() => RunAsync(async () =>
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
    public Task The_megamenu_opens_a_panel_through_the_native_popover() => RunAsync(async () =>
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
    public Task Escape_closes_the_megamenu_because_the_browser_owns_it() => RunAsync(async () =>
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
    public Task Opening_one_panel_closes_the_other() => RunAsync(async () =>
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

        var pager = Page.Locator("[data-testid='ui-pagination-links'] [data-ui-pagination]");

        // Four pages, the first current: 2, 3, 4 and Next are links, and the current page is a marker that is not.
        await Expect(pager.GetByRole(AriaRole.Link)).ToHaveCountAsync(4);
        await Expect(pager.Locator("[aria-current='page']")).ToHaveTextAsync("1");
        await Expect(pager.Locator("button")).ToHaveCountAsync(0);

        var href = await pager.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "2", Exact = true }).GetAttributeAsync("href") ?? "";
        Assert.EndsWith("/ui/navigation/?page=2", href, StringComparison.Ordinal);
    });

    [Fact]
    public Task Choosing_a_page_moves_the_summary_and_the_current_page() => RunAsync(async () =>
    {
        await OpenAsync();
        var pager = Page.Locator("[data-testid='ui-pagination'] [data-ui-pagination]");
        await Expect(pager).ToContainTextAsync("Showing 1 to 5 of 24 results");

        await pager.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "3", Exact = true }).ClickAsync();

        await Expect(pager).ToContainTextAsync("Showing 11 to 15 of 24 results");
        await Expect(pager.Locator("[aria-current='page']")).ToHaveTextAsync("3");
    });

    [Fact]
    public Task Previous_and_next_stop_being_controls_at_either_end() => RunAsync(async () =>
    {
        await OpenAsync();
        var pager = Page.Locator("[data-testid='ui-pagination'] [data-ui-pagination]");
        await Expect(pager.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Previous" })).ToHaveCountAsync(0);

        await pager.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "5", Exact = true }).ClickAsync();

        // On the last page it is Next that is no longer a button, and Previous that is one.
        await Expect(pager).ToContainTextAsync("Showing 21 to 24 of 24 results");
        await Expect(pager.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Next" })).ToHaveCountAsync(0);
        await Expect(pager.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Previous" })).ToHaveCountAsync(1);
        await Expect(pager.Locator("[aria-disabled='true'][aria-label='Next &raquo;']:visible")).ToHaveCountAsync(1);
    });

    [Fact]
    public Task The_pager_draws_at_Flux_sizes_and_numbers_its_pages_from_640px() => RunAsync(async () =>
    {
        await OpenAsync();
        var pager = Page.Locator("[data-testid='ui-pagination'] [data-ui-pagination]");
        await Expect(pager.Locator("[aria-current='page']")).ToBeVisibleAsync();

        var wide = await pager.Locator("[aria-current='page']").BoundingBoxAsync();
        await Page.SetViewportSizeAsync(390, 800);

        try
        {
            // Flux's numbers: a 24px current page; on a phone no numbers at all, and 32px arrows to press.
            Assert.Equal(24, wide!.Height, 1);
            await Expect(pager.Locator("[aria-current='page']")).ToBeHiddenAsync();
            var next = await pager.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Next" }).BoundingBoxAsync();
            Assert.Equal(32, next!.Width, 1);
            Assert.Equal(32, next.Height, 1);
        }
        finally
        {
            await Page.SetViewportSizeAsync(1280, 720);
        }
    });

    [Fact]
    public Task A_simple_pager_has_no_numbers_and_ends_where_there_is_no_more() => RunAsync(async () =>
    {
        await OpenAsync();
        var pager = Page.Locator("[data-testid='ui-pagination-simple'] [data-ui-pagination]");
        await Expect(pager.GetByRole(AriaRole.Button)).ToHaveCountAsync(1);

        // Flux's simple pager names neither arrow, so they are found by place: Previous first, Next last.
        for (var page = 1; page < 5; page++)
        {
            await pager.Locator("button:last-child").ClickAsync();
        }

        // Four presses reach the fifth page, the last one the demo has: Next is spent, Previous is not.
        await Expect(pager.Locator("button:last-child")).ToHaveCountAsync(0);
        await Expect(pager.Locator("button:first-child")).ToHaveCountAsync(1);
        await Expect(pager.Locator("[aria-label], [aria-disabled]")).ToHaveCountAsync(0);
        await Expect(pager).Not.ToContainTextAsync("Showing");
    });

    [Fact]
    public Task A_long_pager_leaves_pages_out_either_side_of_the_current_one() => RunAsync(async () =>
    {
        await OpenAsync();
        var pager = Page.Locator("[data-testid='ui-pagination-large'] [data-ui-pagination]");
        await Expect(pager.GetByText("...", new LocatorGetByTextOptions { Exact = true })).ToHaveCountAsync(1);

        await pager.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "10", Exact = true }).ClickAsync();

        // 1 2 ... 7 8 9 [10] 11 12 13 ... 66 67
        await Expect(pager.GetByText("...", new LocatorGetByTextOptions { Exact = true })).ToHaveCountAsync(2);
        await Expect(pager.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "13", Exact = true })).ToBeVisibleAsync();
        await Expect(pager.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "5", Exact = true })).ToHaveCountAsync(0);
    });

    [Fact]
    public Task Choosing_a_page_scrolls_what_ScrollTo_names_into_view() => RunAsync(async () =>
    {
        await OpenAsync();
        var pager = Page.Locator("[data-testid='ui-pagination-scroll'] [data-ui-pagination]");
        await pager.ScrollIntoViewIfNeededAsync();
        var before = await Page.EvaluateAsync<double>(RowsTop);

        await pager.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Next" }).ClickAsync();

        // The list starts again at the sixth order, and the page has moved it up towards the top of the
        // viewport — as far as the document can scroll, which this near its end is not all the way.
        await Expect(Page.Locator("#ui-pagination-rows")).ToContainTextAsync("Order 6");
        await Page.WaitForFunctionAsync($"() => ({RowsTop})() < {before.ToString(System.Globalization.CultureInfo.InvariantCulture)} - 10",
            null,
            new PageWaitForFunctionOptions { Timeout = 5_000 });
    });

    private const string RowsTop = "() => document.querySelector('#ui-pagination-rows').getBoundingClientRect().top";

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
