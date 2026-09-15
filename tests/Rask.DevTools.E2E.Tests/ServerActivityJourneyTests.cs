using Microsoft.Playwright;
using Rask.DevTools.E2E.Tests.Apps;
using Rask.DevTools.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.DevTools.E2E.Tests;

/// <summary>
///     What a click on a real Server page does to the Renders, Perf and Errors tabs, and to the page itself while the
///     flash is on.
/// </summary>
[Collection(DevToolsHostCollection.Name)]
public sealed class ServerActivityJourneyTests(PlaywrightFixture playwright)
{
    [Fact]
    public async Task A_click_is_counted_on_the_Renders_tab_and_flashes_on_the_page_while_the_switch_is_on()
    {
        await using var host = await DevToolsServerHost.StartAsync<BoardApp>();
        await using var context = await playwright.Browser.NewContextAsync();
        var devTools = await DevToolsPage.OpenAsync(context, host.BaseUrl + "/");
        await devTools.OpenPanelAsync();
        await devTools.ShowTabAsync("Renders");

        await devTools.Panel.GetByRole(AriaRole.Checkbox, new() { Name = "Flash on the page" }).CheckAsync();
        await devTools.Page.WaitForFunctionAsync("() => localStorage.getItem('rask.devtools.flash') === 'on'");
        await devTools.Page.ClickAsync("#add-task");

        // Both colours: the board's render, and the row the click added to the page.
        await devTools.Page.WaitForFunctionAsync(
            """
            () => {
                const root = document.querySelector("rask-devtools")?.shadowRoot;
                const shown = selector => [...(root?.querySelectorAll(selector) ?? [])].filter(b => !b.hidden).length;
                return shown(".fl-render") >= 1 && shown(".fl-dom") >= 1;
            }
            """,
            null,
            new() { Timeout = (float)DevToolsPage.Wait.TotalMilliseconds, PollingInterval = 16 });
        await Expect(devTools.Panel.GetByRole(AriaRole.Cell, new() { Name = "Board", Exact = true }).First)
            .ToBeVisibleAsync(DevToolsPage.Visible);
    }

    [Fact]
    public async Task A_click_is_one_Perf_row_with_the_pages_own_patch_time()
    {
        await using var host = await DevToolsServerHost.StartAsync<BoardApp>();
        await using var context = await playwright.Browser.NewContextAsync();
        var devTools = await DevToolsPage.OpenAsync(context, host.BaseUrl + "/");
        await devTools.OpenPanelAsync();
        await devTools.ShowTabAsync("Perf");

        await devTools.Page.ClickAsync("#add-task");

        var click = devTools.Panel.Locator("tbody tr", new() { HasText = "click" }).First;
        await Expect(click).ToContainTextAsync("Board", DevToolsPage.Contains);
        // "…" stands in for a patch time the page has not reported yet; it is replaced once the page has applied the frame.
        await Expect(click).Not.ToContainTextAsync("…", new() { Timeout = (float)DevToolsPage.Wait.TotalMilliseconds });
        await Expect(click).ToContainTextAsync("ms", DevToolsPage.Contains);
    }

    [Fact]
    public async Task A_handler_that_throws_is_counted_on_the_closed_pill_and_the_overlay_opens_it_on_the_Errors_tab()
    {
        DevToolsGate.AssertOverlayAvailable();
        await using var host = await DevToolsServerHost.StartAsync<BoardApp>();
        await using var context = await playwright.Browser.NewContextAsync();
        var devTools = await DevToolsPage.OpenAsync(context, host.BaseUrl + "/");

        // Opened once and shut, the way a developer leaves it: the count arrives while the drawer is closed.
        await devTools.OpenPanelAsync();
        await devTools.Page.Keyboard.PressAsync("Control+Shift+KeyD");
        await Expect(devTools.Drawer).ToBeHiddenAsync(new() { Timeout = (float)DevToolsPage.Wait.TotalMilliseconds });

        await devTools.Page.ClickAsync("#deploy-button");
        await Expect(devTools.Page.Locator("rask-devtools .dot")).ToHaveTextAsync("1", DevToolsPage.Text);

        var open = devTools.Page.Locator("[data-rask-dev-error] [data-rask-devtools-open]");
        await open.ClickAsync(new() { Timeout = (float)DevToolsPage.Wait.TotalMilliseconds });
        await Expect(devTools.Panel.Locator("[role=tab][aria-selected=\"true\"]")).ToContainTextAsync("Errors", DevToolsPage.Contains);
        var entry = devTools.Panel.Locator("body");
        await Expect(entry).ToContainTextAsync("locked by another deploy", DevToolsPage.Contains);
        await Expect(entry).ToContainTextAsync("DeployCard", DevToolsPage.Contains);
    }
}
