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
    public Task Every_Flux_navigation_piece_renders_with_a_real_size() => RunAsync(async () =>
    {
        await OpenAsync();

        foreach (var id in new[]
                 {
                     "ui-flux-navbar", "ui-flux-navlist", "ui-flux-brand", "ui-flux-profile", "ui-flux-breadcrumbs",
                     "ui-flux-avatar", "ui-flux-avatar-groups",
                 })
        {
            var node = Page.Locator($"[data-testid='{id}']");
            await Expect(node).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            var box = await node.BoundingBoxAsync();
            Assert.True(box!.Height > 0, $"{id} rendered with zero height.");
        }
    });

    [Fact]
    public Task The_current_navbar_item_is_underlined_and_the_others_are_not() => RunAsync(async () =>
    {
        await OpenAsync();

        var nav = Page.Locator("[data-testid='ui-flux-navbar'] nav").First;
        var home = nav.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Home" });
        var pricing = nav.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Pricing" });

        await Expect(home).ToHaveAttributeAsync("aria-current", "page", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });
        await Expect(pricing).Not.ToHaveAttributeAsync("aria-current", "page");

        // Flux's underline: a 2px bar 12px under the item, which is the bottom edge of the navbar.
        Assert.Equal("2px", await home.EvaluateAsync<string>("a => getComputedStyle(a, '::after').height"));
        Assert.Equal("-12px", await home.EvaluateAsync<string>("a => getComputedStyle(a, '::after').bottom"));
        Assert.Equal("none", await pricing.EvaluateAsync<string>("a => getComputedStyle(a, '::after').content"));
    });

    [Fact]
    public Task An_expandable_navlist_group_folds_with_a_click_and_with_the_keyboard() => RunAsync(async () =>
    {
        await OpenAsync();

        var nav = Page.Locator("[data-testid='ui-flux-navlist'] nav").Nth(2);
        var group = nav.Locator("details").First;
        var heading = group.Locator("summary");
        var profile = group.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Profile" });

        // Open unless told otherwise, as Flux's is; the second group on the page was told.
        await Expect(profile).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Expect(nav.Locator("details").Nth(1)).Not.ToHaveAttributeAsync("open", "");

        await heading.ClickAsync();
        await Expect(profile).ToBeHiddenAsync();

        // The heading kept the focus, and Enter and Space both work it: it is a <summary>.
        await Page.Keyboard.PressAsync("Enter");
        await Expect(profile).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Space");
        await Expect(profile).ToBeHiddenAsync();
    });

    [Fact]
    public Task An_avatar_picks_its_own_colour_and_a_group_overlaps_its_faces() => RunAsync(async () =>
    {
        await OpenAsync();

        // "Caleb Porzio" is CP, and CP is emerald: emerald-200 under emerald-800, as on fluxui.dev.
        var caleb = Page.Locator("[data-testid='ui-flux-avatar-auto'] [data-ui-avatar]").First;
        await Expect(caleb).ToHaveTextAsync("CP", new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        Assert.Equal(
            await Page.Locator("[data-testid='ui-flux-avatar-colors'] [data-ui-avatar]").Nth(6)
                .EvaluateAsync<string>("a => getComputedStyle(a).backgroundColor"),
            await caleb.EvaluateAsync<string>("a => getComputedStyle(a).backgroundColor"));

        // Each avatar after the first sits 8px over the one before.
        var faces = Page.Locator("[data-testid='ui-flux-avatar-groups'] [data-ui-avatar]:has-text('3+')").First
            .Locator("xpath=..").Locator("[data-ui-avatar]");
        var first = await faces.Nth(0).BoundingBoxAsync();
        var second = await faces.Nth(1).BoundingBoxAsync();
        Assert.Equal(32, second!.X - first!.X, 1);
    });

    [Fact]
    public Task Breadcrumbs_hide_the_separator_after_the_last_step() => RunAsync(async () =>
    {
        await OpenAsync();

        var trail = Page.Locator("[data-testid='ui-flux-breadcrumbs'] [data-ui-breadcrumbs]").First;
        var items = trail.Locator("[data-ui-breadcrumbs-item]");

        await Expect(items).ToHaveCountAsync(3, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(items.Nth(0).Locator("svg").First).ToBeVisibleAsync();
        await Expect(items.Nth(2).Locator("svg").First).ToBeHiddenAsync();
        await Expect(items.Nth(2)).ToHaveTextAsync("Post");
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

        // A short window: the pager sits near the foot of the document, and in a tall one the list above it
        // is in view however far the page scrolls. Here its first rows can be put above the top edge.
        await Page.SetViewportSizeAsync(1280, 300);

        try
        {
            await pager.ScrollIntoViewIfNeededAsync();
            await Page.EvaluateAsync($"() => scrollBy(0, ({RowsTop})() + 40)");
            var before = await Page.EvaluateAsync<double>(RowsTop);

            await pager.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Next" }).ClickAsync();

            // The list starts again at the sixth order, with its top back at the top of the window.
            Assert.InRange(before, -42, -38);
            await Expect(Page.Locator("#ui-pagination-rows")).ToContainTextAsync("Order 6");
            await Page.WaitForFunctionAsync($"() => Math.abs(({RowsTop})()) < 2", null, new PageWaitForFunctionOptions { Timeout = 5_000 });
        }
        finally
        {
            await Page.SetViewportSizeAsync(1280, 720);
        }
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
