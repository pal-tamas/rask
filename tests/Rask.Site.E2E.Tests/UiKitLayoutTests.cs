using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's layouts in a browser: the header and sidebar pages' framed demos, the demo pages themselves, and the
///     docs' own sidebar, which is the kit's.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitLayoutTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Theory]
    [InlineData("header", "Header", "ui-header|ui-header-inset|ui-header-sidebar")]
    [InlineData("sidebar", "Sidebar", "ui-sidebar|ui-sidebar-inset|ui-sidebar-header|ui-sidebar-collapsible")]
    public Task Every_layout_example_frames_its_demo_drawn_whole(string slug, string heading, string testids) => RunAsync(async () =>
    {
        await OpenUiPageAsync(slug, heading);

        foreach (var id in testids.Split('|'))
        {
            var example = Page.Locator($"[data-testid='{id}']");
            await example.ScrollIntoViewIfNeededAsync();
            var frame = example.FrameLocator("iframe");

            // The demo's own page, laid out as a desktop scaled into the column: its main beside or under the bar.
            await Expect(frame.Locator("[data-ui-main]")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
            await Expect(frame.Locator("[data-ui-sidebar]")).ToHaveCountAsync(1);
            var box = (await example.Locator("iframe").BoundingBoxAsync())!;
            Assert.True(box.Height > 300, $"{id}'s frame is {box.Height}px tall.");
            var width = await frame.Locator("html").EvaluateAsync<int>("() => window.innerWidth");
            Assert.True(width >= 1280, $"{id}'s demo is laid out {width}px wide, narrower than a desktop.");
        }
    });

    [Fact]
    public Task Fullscreen_opens_the_demo_as_a_page_of_its_own() => RunAsync(async () =>
    {
        await OpenUiPageAsync("header", "Header");

        await Page.Locator("[data-testid='ui-header']").GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Fullscreen" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/demo/header/$"),
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });
        // The spacer pushes the account to the far end of the header.
        var header = (await Page.Locator("[data-ui-header]").BoundingBoxAsync())!;
        var account = (await Page.Locator("[data-ui-header] [data-ui-profile]").BoundingBoxAsync())!;
        Assert.True(header.X + header.Width - (account.X + account.Width) < 48, "the spacer did not push the account to the end.");
    });

    [Fact]
    public Task The_old_layout_page_sends_the_reader_on_to_the_header_page() => RunAsync(async () =>
    {
        await Page.GotoAsync($"{Docs}/ui/layout/");

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/ui/header/$"),
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Header",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    });

    [Fact]
    public Task On_a_phone_the_demo_header_slides_the_sidebar_in_and_its_close_control_puts_it_away() => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(390, 844);
        try
        {
            await Page.GotoAsync($"{BaseUrl}/demo/sidebar/");
            await WaitForInteractiveAsync();
            var open = Page.Locator("#sidebar-open");
            var sidebar = Page.Locator("[data-ui-sidebar]");
            await Expect(sidebar).Not.ToBeInViewportAsync(new LocatorAssertionsToBeInViewportOptions { Timeout = 15_000 });

            await Page.Locator("[data-ui-sidebar-toggle]").ClickAsync();
            await Expect(open).ToBeCheckedAsync(new LocatorAssertionsToBeCheckedOptions { Timeout = 15_000 });
            await Expect(sidebar).ToBeInViewportAsync();

            // The close control from the keyboard: a label the runtime presses on Enter.
            await Page.Locator("[data-ui-sidebar-collapse] label[for='sidebar-open']").FocusAsync();
            await Page.Keyboard.PressAsync("Enter");
            await Expect(open).Not.ToBeCheckedAsync(new LocatorAssertionsToBeCheckedOptions { Timeout = 15_000 });
            Assert.Equal(390, await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
        }
        finally
        {
            await Page.SetViewportSizeAsync(1280, 720);
        }
    });

    [Fact]
    public Task On_a_phone_the_secondary_headers_tabs_scroll_inside_their_row_and_the_page_stays_put() => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(390, 844);
        try
        {
            await Page.GotoAsync($"{BaseUrl}/demo/sidebar-with-header/");
            await WaitForInteractiveAsync();

            var tabs = Page.Locator("[data-ui-navbar]").Last;
            await Expect(tabs).ToHaveCSSAsync("overflow-x", "auto", new LocatorAssertionsToHaveCSSOptions { Timeout = 15_000 });

            var row = await tabs.EvaluateAsync<int[]>("n => [n.scrollWidth, n.clientWidth]");
            Assert.True(row[0] > row[1], $"the tabs are {row[0]}px in a {row[1]}px row, so there was nothing to scroll.");
            Assert.Equal(390, await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
        }
        finally
        {
            await Page.SetViewportSizeAsync(1280, 720);
        }
    });

    [Fact]
    public Task The_headers_icon_only_items_carry_Fluxs_label_and_are_forty_pixels_wide() => RunAsync(async () =>
    {
        await Page.GotoAsync($"{BaseUrl}/demo/header/");
        await WaitForInteractiveAsync();

        var search = Page.Locator("[data-ui-navbar-items][label='Search']");

        await Expect(search).ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        Assert.Equal(40, (int)Math.Round((await search.BoundingBoxAsync())!.Width));
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
    public Task A_collapsed_rail_is_still_collapsed_after_a_reload_and_never_drawn_wide() =>
        RunAsync(() => ReloadCollapsedAsync(holdHooksFor: TimeSpan.Zero));

    [Fact]
    public Task A_collapsed_rail_is_never_drawn_wide_however_late_the_behaviour_hooks_arrive() =>
        RunAsync(() => ReloadCollapsedAsync(holdHooksFor: TimeSpan.FromSeconds(4)));

    // Every frame the sidebar is painted in after a reload, with what could have made it wide: how far the
    // document had got, whether the hooks bundle had run, whether the app had rendered over the prerendered
    // page (the box is then another element), and whether the box was checked.
    private const string FrameRecorder =
        "window.__frames=[];(function f(){var s=document.querySelector('[data-ui-sidebar]');"
        + "if(s){var b=document.getElementById('sidebar-rail');"
        + "if(b&&!window.__box)window.__box=b;"
        + "window.__frames.push(Math.round(s.getBoundingClientRect().width)+' '+document.readyState"
        + "+(performance.getEntriesByType('resource').some(function(e){return e.name.indexOf('rask-hooks')>=0&&e.responseEnd>0})?' hooks':' no-hooks')"
        + "+(b===window.__box?' prerendered-box':' app-box')+(b&&b.checked?' checked':' unchecked'));}"
        + "requestAnimationFrame(f);})();";

    private async Task ReloadCollapsedAsync(TimeSpan holdHooksFor)
    {
        // Reached from a page of the running app, so the runtime that stores the choice is there to hear it.
        await OpenSidebarDemoAsync();
        var rail = Page.Locator("#sidebar-rail");
        var sidebar = Page.Locator("[data-ui-sidebar]");
        await Page.Locator("[data-ui-sidebar-collapse] label:visible").First.ClickAsync();
        await Expect(rail).ToBeCheckedAsync(new LocatorAssertionsToBeCheckedOptions { Timeout = 15_000 });
        await Page.WaitForFunctionAsync("() => localStorage.getItem('flux-sidebar-collapsed-desktop') === 'true'");

        // Every width the sidebar is painted at after the reload, from the first frame on: a flash would be a wide one.
        await Page.AddInitScriptAsync(FrameRecorder);
        if (holdHooksFor > TimeSpan.Zero)
        {
            // A slow network for that one file: the app boots and renders long before its hooks are there.
            await Page.RouteAsync("**/rask-hooks.js*", async route =>
            {
                await Task.Delay(holdHooksFor);
                await route.ContinueAsync();
            });
        }

        await Page.ReloadAsync();

        await Expect(rail).ToBeCheckedAsync(new LocatorAssertionsToBeCheckedOptions { Timeout = 15_000 });
        await Expect(sidebar).ToHaveCSSAsync("width", "56px");
        // Past the hooks' arrival, which is the last thing that could change the box.
        await Page.WaitForFunctionAsync(
            "() => window.__frames.some(f => f.includes(' hooks'))",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        await Page.WaitForTimeoutAsync(300);
        var frames = await Page.EvaluateAsync<string[]>("() => window.__frames");
        Assert.NotEmpty(frames);
        var wide = frames.Select((frame, index) => (frame, index)).Where(f => !f.frame.StartsWith("56 ", StringComparison.Ordinal)).ToList();
        Assert.True(
            wide.Count == 0,
            $"The rail was drawn wide in {wide.Count} of {frames.Length} frames after the reload. Each frame is "
            + "\"width readyState hooks-bundle box checked\": a wide frame with 'prerendered-box unchecked' is the head "
            + "script late (Ui.SidebarScript); one with 'app-box unchecked' is the app's own render putting an unchecked "
            + "box over the restored one; 'no-hooks' says the hooks bundle had not run yet."
            + Environment.NewLine + string.Join(Environment.NewLine, wide.Take(12).Select(f => $"  frame {f.index}: {f.frame}"))
            + Environment.NewLine + "  first frames: " + string.Join(" | ", frames.Take(4))
            + Environment.NewLine + "  last frame: " + frames[^1]);
    }

    [Fact]
    public Task Headings_are_drawn_as_Flux_draws_them_with_the_level_apart_from_the_size() => RunAsync(async () =>
    {
        await OpenUiPageAsync("heading", "Heading");

        var scope = Page.Locator("[data-testid^='ui-heading-']");
        await Expect(scope.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        await Expect(scope.Locator("h3[data-ui-heading]", new LocatorLocatorOptions { HasText = "User profile" })).ToHaveCountAsync(1);
        await Expect(scope.GetByText("Extra extra large")).ToHaveCSSAsync("font-size", "36px");
    });

    [Fact]
    public Task Text_and_links_are_drawn_as_Flux_draws_them() => RunAsync(async () =>
    {
        await OpenUiPageAsync("text", "Text");

        var scope = Page.Locator("[data-testid^='ui-text-']");
        await Expect(scope.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

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
    public Task A_checked_control_is_actually_checked_in_the_DOM() => RunAsync(async () =>
    {
        // The regression this guards is the one markup could not show: the kit wrote the checked state
        // into the VALUE attribute, so a control the page said was on arrived off. Asserted through the
        // DOM's own property rather than the attribute, which is what a reader actually sees.
        await OpenUiPageAsync("checkbox", "Checkbox");
        var enabled = Page.Locator("[data-testid='ui-checkbox-checked'] input[type='checkbox']");
        await Expect(enabled).ToBeCheckedAsync();

        await OpenUiPageAsync("switch", "Switch");
        var notify = Page.Locator("[data-testid='ui-switch'] #sw-notify");
        await Expect(notify).Not.ToBeCheckedAsync();
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
        await Expect(bubble).ToHaveTextAsync("Inbox");
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
        await Expect(menu.GetByRole(AriaRole.Menuitem)).ToHaveTextAsync(["Marketing site", "Android app", "Brand guidelines"]);
        await Expect(menu.Locator("[data-ui-menu-heading]")).ToHaveTextAsync("Favorites");
        var panel = (await menu.BoundingBoxAsync())!;
        Assert.Equal(icon.X + icon.Width + 5, panel.X, 1.0);
        Assert.Equal(icon.Y, panel.Y, 1.0);
        await Page.Mouse.MoveAsync(panel.X + 40, icon.Y + (icon.Height / 2));
        await Expect(menu).ToBeVisibleAsync();
        // A move is looked at once in 100 ms, as on Flux, and the one onto the menu was: the pointer rests first.
        await Page.WaitForTimeoutAsync(150);
        await Page.Mouse.MoveAsync(900, 500);
        await Expect(menu).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15_000 });

        // The keyboard: Enter opens it, the arrows walk its rows, Escape hands the focus back to the icon.
        await button.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(menu).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(menu.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Marketing site" })).ToBeFocusedAsync(
            new LocatorAssertionsToBeFocusedOptions { Timeout = 15_000 });
        await Page.Keyboard.PressAsync("Escape");
        await Expect(menu).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15_000 });
        await Expect(button).ToBeFocusedAsync();

        // A row is the group's own link: picking it follows it.
        await Page.Keyboard.PressAsync("Enter");
        await Expect(menu.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Android app" }))
            .ToHaveAttributeAsync("href", "#");
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
        await Page.GotoAsync($"{BaseUrl}/demo/sidebar-collapsible/", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
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

    // The collapsible sidebar's demo as a page of its own, reached from its Fullscreen on the sidebar page.
    private async Task OpenSidebarDemoAsync()
    {
        await OpenUiPageAsync("sidebar", "Sidebar");
        await Page.EvaluateAsync("() => localStorage.removeItem('flux-sidebar-collapsed-desktop')");
        await Page.Locator("[data-testid='ui-sidebar-collapsible']")
            .GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Fullscreen" }).ClickAsync();
        await Expect(Page.Locator("[data-ui-sidebar]")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await WaitForInteractiveAsync();
    }

    // The docs, whose own sidebar is the kit's: a page with it slid away on a phone.
    private async Task OpenAsync()
    {
        await Page.GotoAsync($"{Docs}/");
        await Expect(Page.Locator(".side-nav a.side-nav-link[aria-current='page']").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await WaitForInteractiveAsync();
    }
}
