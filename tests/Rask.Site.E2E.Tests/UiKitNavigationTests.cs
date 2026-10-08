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
    public Task Every_Flux_tabs_example_is_on_the_page() => RunAsync(async () =>
    {
        await OpenAsync();

        string[] examples =
        [
            "ui-tabs", "ui-tabs-findable", "ui-tabs-icons", "ui-tabs-padded", "ui-tabs-scrollable", "ui-tabs-fade",
            "ui-tabs-segmented", "ui-tabs-segmented-icons", "ui-tabs-segmented-sm", "ui-tabs-pills", "ui-tabs-dynamic",
        ];

        foreach (var example in examples)
        {
            var row = Page.Locator($"[data-testid='{example}'] [role='tablist']");
            await Expect(row).ToBeVisibleAsync();
            await Expect(row.Locator("[role='tab'][aria-selected='true']")).ToHaveCountAsync(1);
        }
    });

    [Fact]
    public Task Clicking_a_tab_shows_its_panel_and_tells_the_page_its_name() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-tabs']");

        await scope.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "Account" }).ClickAsync();

        await Expect(scope.Locator("[data-testid='ui-tabs-state']")).ToHaveTextAsync("Selected: account.");
        await Expect(scope.GetByRole(AriaRole.Tabpanel)).ToHaveTextAsync("Your email address and how you sign in.");
        await Expect(scope.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "Account" }))
            .ToHaveAttributeAsync("tabindex", "0");
        await Expect(scope.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "Profile" }))
            .ToHaveAttributeAsync("tabindex", "-1");
    });

    [Fact]
    public Task The_arrow_keys_move_focus_and_select_as_they_go_around_the_ends() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-tabs']");
        var state = scope.Locator("[data-testid='ui-tabs-state']");
        await scope.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "Profile" }).FocusAsync();

        // Selection follows focus, as it does in Flux: no Enter, no Space.
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(state).ToHaveTextAsync("Selected: account.");
        await Expect(scope.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "Account" })).ToBeFocusedAsync();

        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(state).ToHaveTextAsync("Selected: billing.");

        // Past the last tab is the first, and back again.
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(state).ToHaveTextAsync("Selected: profile.");
        await Page.Keyboard.PressAsync("ArrowLeft");
        await Expect(state).ToHaveTextAsync("Selected: billing.");
        await Page.Keyboard.PressAsync("ArrowUp");
        await Expect(state).ToHaveTextAsync("Selected: account.");
        await Expect(scope.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "Account" })).ToBeFocusedAsync();
    });

    [Fact]
    public Task Tab_leaves_the_row_for_the_panel_it_opened() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-tabs']");
        await scope.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "Profile" }).FocusAsync();

        // One tab stop in the row: the next is the panel, not the tab beside it.
        await Page.Keyboard.PressAsync("Tab");

        await Expect(scope.GetByRole(AriaRole.Tabpanel)).ToBeFocusedAsync();
    });

    [Fact]
    public Task The_arrow_keys_pass_over_a_disabled_tab() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-tabs-disabled']");
        var (open, archived, failed) = (scope.Locator("[role='tab']").Nth(0), scope.Locator("[role='tab']").Nth(1), scope.Locator("[role='tab']").Nth(2));
        await Expect(archived).ToBeDisabledAsync();
        await open.FocusAsync();

        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(failed).ToBeFocusedAsync();
        await Expect(failed).ToHaveAttributeAsync("aria-selected", "true");

        await Page.Keyboard.PressAsync("ArrowLeft");
        await Expect(open).ToBeFocusedAsync();
        await Expect(open).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(archived).ToHaveAttributeAsync("aria-selected", "false");
    });

    [Fact]
    public Task A_bound_segmented_row_selects_what_was_clicked_and_stays_on_it() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-tabs-segmented']");
        var board = scope.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "Board" });

        await board.ClickAsync();
        await Expect(board).ToHaveAttributeAsync("aria-selected", "true");

        // Bound to a property of the page: the row reads the selection back from it on every render, so a
        // re-render caused elsewhere on the page must leave it where it was put.
        await Page.Locator("[data-testid='ui-tabs']").GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "Billing" }).ClickAsync();
        await Expect(board).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(scope.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "List" }))
            .ToHaveAttributeAsync("aria-selected", "false");
    });

    [Fact]
    public Task The_add_action_adds_a_tab_and_selects_nothing() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-tabs-dynamic']");
        await Expect(scope.GetByRole(AriaRole.Tab)).ToHaveCountAsync(2);

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add tab" }).ClickAsync();

        await Expect(scope.GetByRole(AriaRole.Tab)).ToHaveCountAsync(3);
        await Expect(scope.Locator("[role='tab'][aria-selected='true']")).ToHaveTextAsync("Tab #1");

        // The new tab has a panel of its own, and the arrows reach it without stopping on the action.
        await scope.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = "Tab #3" }).ClickAsync();
        await Expect(scope.GetByRole(AriaRole.Tabpanel)).ToHaveTextAsync("What Tab #3 holds.");
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(scope.Locator("[role='tab'][aria-selected='true']")).ToHaveTextAsync("Tab #1");
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
