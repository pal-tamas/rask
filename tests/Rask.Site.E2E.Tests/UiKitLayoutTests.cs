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
    public Task Every_layout_component_renders_with_a_real_size() => RunAsync(async () =>
    {
        await OpenAsync();

        foreach (var id in new[]
                 {
                     "ui-drawer", "ui-separator", "ui-layout-rest", "ui-typography", "ui-layout-mask", "ui-mockups",
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
    public Task The_Flux_layout_pieces_work() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-app-layout']");
        // The sidebar in the box: the two buttons above it also say "layout".
        var box = Page.Locator("[data-testid='ui-layout-box']");

        // The item for this page is current without being told, and says so to assistive tech.
        await Expect(box.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Layout", Exact = true }))
            .ToHaveAttributeAsync("aria-current", "page", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });
        await Expect(box.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Actions 5" }))
            .Not.ToHaveAttributeAsync("aria-current", "page");

        // The spacer pushes "Sign in" to the far end of its row.
        var row = await Page.Locator("[data-testid='ui-spacer-row']").BoundingBoxAsync();
        var signIn = await Page.Locator("[data-testid='ui-spacer-row']")
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Sign in" }).BoundingBoxAsync();
        Assert.True(row!.X + row.Width - (signIn!.X + signIn.Width) < 16, "the spacer did not push the last button to the end.");

        // The separator has no margin of its own: the page spaces it.
        var margin = await scope.Locator("[data-ui-separator]").Last.EvaluateAsync<string>("d => getComputedStyle(d).marginTop");
        Assert.Equal("0px", margin);
    });

    [Fact]
    public Task The_sidebar_slid_over_a_phone_is_put_away_when_the_reader_goes_somewhere() => RunAsync(async () =>
    {
        // Opened at a desktop width, where the sidebar is docked and its links can be pressed; then a phone's.
        await OpenAsync();
        await Page.SetViewportSizeAsync(390, 800);
        var open = Page.Locator("#sidebar-open");
        await Page.Locator("[data-ui-sidebar-toggle]").First.ClickAsync();
        await Expect(open).ToBeCheckedAsync(new LocatorAssertionsToBeCheckedOptions { Timeout = 15_000 });

        var before = Page.Url;
        // Pressed in the page: the sidebar scrolls inside itself, and which link is in view is not the point.
        await Page.EvaluateAsync("() => document.querySelector(\"[data-ui-sidebar] a[data-ui-sidebar-item]:not([aria-current='page'])\").click()");

        await Expect(Page).Not.ToHaveURLAsync(before, new PageAssertionsToHaveURLOptions { Timeout = 15_000 });
        await Expect(open).Not.ToBeCheckedAsync(new LocatorAssertionsToBeCheckedOptions { Timeout = 15_000 });
    });

    [Fact]
    public Task The_sidebar_slid_over_a_phone_is_put_away_when_the_page_already_open_is_chosen() => RunAsync(async () =>
    {
        await OpenAsync();
        await Page.SetViewportSizeAsync(390, 844);
        var open = Page.Locator("#sidebar-open");
        await Page.Locator("[data-ui-sidebar-toggle]").First.ClickAsync();
        await Expect(open).ToBeCheckedAsync(new LocatorAssertionsToBeCheckedOptions { Timeout = 15_000 });

        var before = Page.Url;
        // The row of the page the reader is on: it navigates nowhere, and the sidebar must not stay over the page.
        await Page.EvaluateAsync("() => document.querySelector(\"[data-ui-sidebar] a[data-ui-sidebar-item][aria-current='page']\").click()");

        await Expect(open).Not.ToBeCheckedAsync(new LocatorAssertionsToBeCheckedOptions { Timeout = 15_000 });
        await Expect(Page.Locator("[data-ui-sidebar-backdrop]").First).ToBeHiddenAsync();
        Assert.Equal(before, Page.Url);
    });

    [Fact]
    public Task A_collapsed_rail_is_still_collapsed_after_a_reload_and_never_drawn_wide() => RunAsync(async () =>
    {
        // Reached from a page of the running app, so the runtime that stores the choice is there to hear it.
        await OpenAsync();
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Open the sidebar layout" }).ClickAsync();
        await Expect(Page.Locator("[data-testid='sidebar-layout-demo']")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        var rail = Page.Locator("#sidebar-rail");
        var sidebar = Page.Locator("[data-ui-sidebar]");
        await Page.Locator("[data-ui-sidebar-collapse] label:visible").First.ClickAsync();
        await Expect(rail).ToBeCheckedAsync(new LocatorAssertionsToBeCheckedOptions { Timeout = 15_000 });
        await Page.WaitForFunctionAsync("() => localStorage.getItem('flux-sidebar-collapsed-desktop') === 'true'");

        // Every width the sidebar is painted at after the reload, from the first frame on: a flash would be a wide one.
        await Page.AddInitScriptAsync(
            "window.__widths=[];(function f(){var s=document.querySelector('[data-ui-sidebar]');"
            + "if(s)window.__widths.push(Math.round(s.getBoundingClientRect().width));requestAnimationFrame(f);})();");
        await Page.ReloadAsync();

        await Expect(rail).ToBeCheckedAsync(new LocatorAssertionsToBeCheckedOptions { Timeout = 15_000 });
        await Expect(sidebar).ToHaveCSSAsync("width", "56px");
        var widths = await Page.EvaluateAsync<int[]>("() => window.__widths");
        Assert.NotEmpty(widths);
        Assert.All(widths, width => Assert.Equal(56, width));
    });

    [Fact]
    public Task Headings_text_and_links_are_drawn_as_Flux_draws_them() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-typography']");
        await Expect(scope).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        await Expect(scope.Locator("h3[data-ui-heading]")).ToHaveTextAsync("User profile");
        await Expect(scope.GetByText("Extra extra large")).ToHaveCSSAsync("font-size", "36px");
        await Expect(scope.GetByText("Smaller text")).ToHaveCSSAsync("font-size", "12px");
        await Expect(scope.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Default link" }))
            .ToHaveCSSAsync("text-underline-offset", "6px");
        await Expect(scope.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "documentation" }))
            .ToHaveAttributeAsync("rel", "noopener noreferrer");
        await Expect(scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create new account →" }))
            .ToHaveAttributeAsync("type", "button");
    });

    [Fact]
    public Task The_docs_sidebar_is_the_kits_sidebar_and_opens_from_the_keyboard_on_a_phone() => RunAsync(async () =>
    {
        await OpenAsync();

        await Page.SetViewportSizeAsync(390, 844);
        try
        {
            var sideNav = Page.Locator(".side-nav");
            await Expect(sideNav).Not.ToBeInViewportAsync(new LocatorAssertionsToBeInViewportOptions { Timeout = 10_000 });

            // The hamburger is a label for the sidebar's checkbox: a keyboard stop the runtime presses on Enter.
            await Page.Locator(".hamburger-btn").FocusAsync();
            await Page.Keyboard.PressAsync("Enter");

            await Expect(sideNav).ToBeInViewportAsync(new LocatorAssertionsToBeInViewportOptions { Timeout = 10_000 });
            await Expect(Page.Locator(".side-nav nav[data-ui-sidebar-nav]")).ToHaveCountAsync(1);
        }
        finally
        {
            await Page.SetViewportSizeAsync(1280, 720);
        }
    });

    [Fact]
    public Task The_drawer_opens_and_the_page_is_told_about_it() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-drawer']");
        var state = Page.Locator("[data-testid='ui-drawer-state']");

        await Expect(state).ToContainTextAsync("closed");

        await scope.GetByText("Open the drawer").ClickAsync();

        // Both halves: Ui.Modal's flyout is against the left edge, and the PAGE knows it is open.
        var flyout = scope.Locator("dialog");
        await Expect(state).ToContainTextAsync("open");
        await Expect(scope.GetByText("Queues")).ToBeVisibleAsync();
        await Page.WaitForTimeoutAsync(300);
        Assert.True((await flyout.BoundingBoxAsync())!.X <= 2, "the flyout is not against the left edge.");

        // And it hears the reader close it, which is how the page stops rendering it open.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(state).ToContainTextAsync("closed");
        await Expect(flyout).ToBeHiddenAsync();
    });


    [Fact]
    public Task A_checked_control_is_actually_checked_in_the_DOM() => RunAsync(async () =>
    {
        // The regression this guards is the one markup could not show: the kit wrote the checked state
        // into the VALUE attribute, so a control the page said was on arrived off. Asserted through the
        // DOM's own property rather than the attribute, which is what a reader actually sees.
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link[aria-current='page']").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await ClickSidebar("Data input");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Data input",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        var enabled = Page.Locator("[data-testid='ui-checkbox'] #cb-checked");
        await Expect(enabled).ToBeCheckedAsync();

        var notify = Page.Locator("[data-testid='ui-switch'] #sw-notify");
        await Expect(notify).Not.ToBeCheckedAsync();
    });

    [Fact]
    public Task The_code_mockup_shows_its_prefixes_and_its_text() => RunAsync(async () =>
    {
        await OpenAsync();

        var code = Page.Locator("[data-testid='ui-mockups'] .mockup-code");

        await Expect(code).ToBeVisibleAsync();
        await Expect(code).ToContainTextAsync("rask new shop");
        await Expect(code.Locator("pre[data-prefix='$']")).ToHaveCountAsync(2);
    });

    [Fact]
    public Task A_collapsed_rail_says_an_items_label_in_a_tooltip_beside_it_and_a_wide_sidebar_does_not() => RunAsync(async () =>
    {
        await OpenSidebarDemoAsync();
        var tooltip = Page.Locator("[data-ui-sidebar-nav] > [data-ui-tooltip]").Nth(1);
        var item = tooltip.Locator("a[data-ui-sidebar-item]");
        var bubble = tooltip.Locator("[data-ui-tooltip-content]");

        await item.HoverAsync();
        await Expect(bubble).ToBeHiddenAsync();
        await Page.Locator("[data-ui-sidebar-collapse] label:visible").First.ClickAsync();
        await Expect(Page.Locator("[data-ui-sidebar]")).ToHaveCSSAsync("width", "56px");
        await Page.Mouse.MoveAsync(900, 500);
        await item.HoverAsync();

        await Expect(bubble).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Expect(bubble).ToHaveTextAsync("Header layout");
        await Expect(item).ToHaveAttributeAsync("aria-describedby", await bubble.GetAttributeAsync("id") ?? "");
        var (link, tip) = ((await item.BoundingBoxAsync())!, (await bubble.BoundingBoxAsync())!);
        Assert.Equal(link.X + link.Width + 5, tip.X, 1.0);
        Assert.Equal(link.Y + (link.Height / 2), tip.Y + (tip.Height / 2), 1.0);
    });

    [Fact]
    public Task A_group_in_the_rail_opens_its_items_as_a_menu_under_the_pointer_and_from_the_keyboard() => RunAsync(async () =>
    {
        await OpenSidebarDemoAsync();
        var group = Page.Locator("[data-ui-sidebar-group-dropdown]");
        var button = group.Locator("> button");
        var menu = group.Locator("[role='menu']");
        await Expect(button).ToBeHiddenAsync();
        await Page.Locator("[data-ui-sidebar-collapse] label:visible").First.ClickAsync();
        await Expect(Page.Locator("[data-ui-sidebar]")).ToHaveCSSAsync("width", "56px");
        await Page.Mouse.MoveAsync(900, 500);

        // The pointer: open while it is on the icon or the menu, shut once it is on neither.
        var icon = (await button.BoundingBoxAsync())!;
        await Page.Mouse.MoveAsync(icon.X + (icon.Width / 2), icon.Y + (icon.Height / 2), new MouseMoveOptions { Steps = 4 });
        await Expect(menu).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Expect(menu.GetByRole(AriaRole.Menuitem)).ToHaveTextAsync(["Actions", "Navigation"]);
        await Expect(menu.Locator("[data-ui-menu-heading]")).ToHaveTextAsync("Favorites");
        var panel = (await menu.BoundingBoxAsync())!;
        Assert.Equal(icon.X + icon.Width + 5, panel.X, 1.0);
        Assert.Equal(icon.Y, panel.Y, 1.0);
        await Page.Mouse.MoveAsync(panel.X + 40, icon.Y + (icon.Height / 2));
        await Expect(menu).ToBeVisibleAsync();
        await Page.Mouse.MoveAsync(900, 500);
        await Expect(menu).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15_000 });

        // The keyboard: Enter opens it, the arrows walk its rows, Escape hands the focus back to the icon.
        await button.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(menu).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(menu.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Actions" })).ToBeFocusedAsync(
            new LocatorAssertionsToBeFocusedOptions { Timeout = 15_000 });
        await Page.Keyboard.PressAsync("Escape");
        await Expect(menu).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15_000 });
        await Expect(button).ToBeFocusedAsync();

        // A row is the group's own link: picking it goes there.
        await Page.Keyboard.PressAsync("Enter");
        await menu.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Navigation" }).ClickAsync();
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Navigation",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    });

    [Fact]
    public Task A_press_on_the_collapse_control_before_the_hooks_have_loaded_is_remembered() => RunAsync(async () =>
    {
        // A cold load of the page itself, with the runtime held back — and the hook bundle with it, which the
        // runtime is what asks for: the gap in which the prerendered page can be pressed and nothing but the
        // head script is there to hear it.
        var release = new TaskCompletionSource();
        var asked = new TaskCompletionSource();
        await Page.RouteAsync("**/main.js*", async route =>
        {
            asked.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        // The held script holds the load event too, so the navigation is over once the document is.
        await Page.GotoAsync($"{BaseUrl}/demo/sidebar", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        var rail = Page.Locator("#sidebar-rail");
        var control = Page.Locator("[data-ui-sidebar-collapse] label:visible").First;
        await Expect(control).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await control.ClickAsync();
        await Expect(rail).ToBeCheckedAsync();
        var stored = await Page.EvaluateAsync<string?>("() => localStorage.getItem('flux-sidebar-collapsed-desktop')");
        release.SetResult();
        // The runtime was asked for and held, or this proved nothing.
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        Assert.Equal("true", stored);
        await Expect(rail).ToBeCheckedAsync();
        await Expect(Page.Locator("[data-ui-sidebar]")).ToHaveCSSAsync("width", "56px");
    });

    // The sidebar layout as a page of its own, reached from the docs so the app is running when it opens.
    private async Task OpenSidebarDemoAsync()
    {
        await OpenAsync();
        await Page.EvaluateAsync("() => localStorage.removeItem('flux-sidebar-collapsed-desktop')");
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Open the sidebar layout" }).ClickAsync();
        await Expect(Page.Locator("[data-testid='sidebar-layout-demo']")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
    }

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link[aria-current='page']").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Layout & mockups");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Layout",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }
}
