using Microsoft.Playwright;
using Rask.DevTools.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.DevTools.E2E.Tests;

/// <summary>
///     The devtools on a WASM page: the panel is a second session in the page's own runtime, drawn into a frame the page
///     writes, with nothing on a server to ask. The same pill, the same tabs, the same page reading.
/// </summary>
[Collection(DevToolsHostCollection.Name)]
public sealed class WasmJourneyTests(PlaywrightFixture playwright, WasmFixtureHost host) : IClassFixture<WasmFixtureHost>
{
    [Fact]
    public async Task The_panel_lists_the_pages_click_and_the_frame_that_answered_it_and_the_shortcut_closes_it_from_inside()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var devTools = await DevToolsPage.OpenAsync(context, host.BaseUrl + "/");
        await devTools.OpenPanelAsync();

        await devTools.Page.ClickAsync("#fixture-click");
        await Expect(devTools.Page.Locator("#fixture-counter p")).ToHaveTextAsync("clicks=1", DevToolsPage.Text);
        await Expect(devTools.Panel.Locator("tbody tr", new() { HasText = "click" }).First).ToBeVisibleAsync(DevToolsPage.Visible);
        await Expect(devTools.Panel.Locator("tbody tr", new() { HasText = "frame" }).First).ToBeVisibleAsync(DevToolsPage.Visible);

        // The shortcut pressed with the focus inside the panel's own frame still reaches the page's dock.
        await devTools.Tab("Wire").FocusAsync();
        await devTools.Page.Keyboard.PressAsync("Control+Shift+KeyD");
        await Expect(devTools.Drawer).ToBeHiddenAsync(new() { Timeout = (float)DevToolsPage.Wait.TotalMilliseconds });
    }

    [Fact]
    public async Task A_row_boxes_its_component_and_a_pick_selects_it_without_the_page_counting_the_click()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var devTools = await DevToolsPage.OpenAsync(context, host.BaseUrl + "/");
        await devTools.OpenPanelAsync();
        await devTools.ShowTabAsync("Tree");

        var row = devTools.Panel.Locator("[role=treeitem]", new() { HasText = "FixtureCounter" }).Last.Locator(".ui-tree-row").First;
        await row.HoverAsync();
        await Expect(devTools.Page.Locator("rask-devtools .hl-label"))
            .ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^FixtureCounter"), DevToolsPage.Text);
        var box = await devTools.Page.Locator("rask-devtools .hl").BoundingBoxAsync();
        var counter = await devTools.Page.Locator("#fixture-counter").BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.NotNull(counter);
        Assert.InRange(box!.Y, counter!.Y - 1, counter.Y + 1);
        Assert.InRange(box.Height, counter.Height - 2, counter.Height + 2);

        var button = await devTools.Page.Locator("#fixture-click").BoundingBoxAsync();
        await devTools.Panel.GetByRole(AriaRole.Button, new() { Name = "Pick" }).ClickAsync();
        await Expect(devTools.Panel.Locator("[aria-pressed=\"true\"]", new() { HasText = "Pick" })).ToBeVisibleAsync(DevToolsPage.Visible);
        var (x, y) = (button!.X + (button.Width / 2), button.Y + (button.Height / 2));
        await devTools.Page.Mouse.MoveAsync(x, y, new() { Steps = 4 });
        await Expect(devTools.Page.Locator("rask-devtools .hl-label"))
            .ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^FixtureCounter"), DevToolsPage.Text);
        await devTools.Page.Mouse.ClickAsync(x, y);

        await Expect(devTools.Panel.Locator("[data-rask-devtools-details]")).ToContainTextAsync("Step", DevToolsPage.Contains);
        await Expect(devTools.Page.Locator("#fixture-counter p")).ToHaveTextAsync("clicks=0");
    }

    [Fact]
    public async Task A_handler_that_throws_is_counted_on_the_pill_and_listed_on_the_Errors_tab()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var devTools = await DevToolsPage.OpenAsync(context, host.BaseUrl + "/");
        await devTools.OpenPanelAsync();
        await devTools.Page.Keyboard.PressAsync("Control+Shift+KeyD");
        await Expect(devTools.Drawer).ToBeHiddenAsync(new() { Timeout = (float)DevToolsPage.Wait.TotalMilliseconds });

        await devTools.Page.ClickAsync("#fixture-throw");
        await Expect(devTools.Page.Locator("rask-devtools .dot")).ToHaveTextAsync("1", DevToolsPage.Text);

        await devTools.Pill.ClickAsync();
        await devTools.ShowTabAsync("Errors");
        var errors = devTools.Panel.Locator("body");
        await Expect(errors).ToContainTextAsync("failed on purpose", DevToolsPage.Contains);
        await Expect(errors).ToContainTextAsync("App", DevToolsPage.Contains);
    }
}
