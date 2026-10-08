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
