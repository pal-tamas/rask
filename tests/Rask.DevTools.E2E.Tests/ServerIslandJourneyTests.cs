using Microsoft.Playwright;
using Rask.DevTools.E2E.Tests.Apps;
using Rask.DevTools.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.DevTools.E2E.Tests;

/// <summary>
///     Real islands under the devtools: a Lit-runtime island and a Blazor component as badged rows with their props, a
///     failing island on the pill and the Errors tab, and a Blazor component's own click on the Wire tab.
/// </summary>
[Collection(DevToolsHostCollection.Name)]
public sealed class ServerIslandJourneyTests(PlaywrightFixture playwright)
{
    [Fact]
    public async Task Each_island_is_a_tree_row_badged_with_its_kind_and_carrying_the_props_CSharp_passed()
    {
        await using var host = await DevToolsServerHost.StartAsync<IslandsApp>(islands: true);
        await using var context = await playwright.Browser.NewContextAsync();
        var devTools = await DevToolsPage.OpenAsync(context, host.BaseUrl + "/");
        // The Lit island really loaded: its element rendered its props.
        await Expect(devTools.Page.Locator("#lit devtools-e2e-meter")).ToHaveTextAsync("CPU: 42", DevToolsPage.Text);

        await devTools.OpenPanelAsync();
        await devTools.ShowTabAsync("Tree");
        var meter = devTools.Panel.Locator("[role=treeitem]", new() { HasText = "Label=CPU" }).Last;
        await Expect(meter).ToContainTextAsync("DevToolsMeter", DevToolsPage.Contains);
        await Expect(meter).ToContainTextAsync("Lit", DevToolsPage.Contains);
        var ticker = devTools.Panel.Locator("[role=treeitem]", new() { HasText = "DevToolsTicker" }).Last;
        await Expect(ticker).ToContainTextAsync("Blazor", DevToolsPage.Contains);

        await meter.Locator(".ui-tree-row").First.ClickAsync();
        var details = devTools.Panel.Locator("[data-rask-devtools-details]");
        await Expect(details).ToContainTextAsync("An island", DevToolsPage.Contains);
        await Expect(details).ToContainTextAsync("Value", DevToolsPage.Contains);
    }

    [Fact]
    public async Task An_island_that_fails_to_mount_is_counted_before_the_panel_opens_and_listed_when_it_does()
    {
        await using var host = await DevToolsServerHost.StartAsync<IslandsApp>(islands: true);
        await using var context = await playwright.Browser.NewContextAsync();
        var devTools = await DevToolsPage.OpenAsync(context, host.BaseUrl + "/");

        // Never opened: the page's own failures are counted on the pill as they happen.
        await Expect(devTools.Page.Locator("rask-devtools .dot")).ToHaveTextAsync("1", DevToolsPage.Text);

        await devTools.OpenPanelAsync();
        await devTools.ShowTabAsync("Errors");
        var errors = devTools.Panel.Locator("body");
        // An island's own failure, from the island runtime, not a page error that happens to share the message.
        await Expect(errors).ToContainTextAsync("failed to mount: the meter island failed on purpose", DevToolsPage.Contains);
        await Expect(errors).ToContainTextAsync("DevToolsMeter", DevToolsPage.Contains);
    }

    [Fact]
    public async Task A_Blazor_components_own_click_reaches_its_callback_and_is_listed_on_the_Wire_tab()
    {
        await using var host = await DevToolsServerHost.StartAsync<IslandsApp>(islands: true);
        await using var context = await playwright.Browser.NewContextAsync();
        var devTools = await DevToolsPage.OpenAsync(context, host.BaseUrl + "/");
        await devTools.OpenPanelAsync();

        await devTools.Page.ClickAsync("#blazor .ticker strong");

        await Expect(devTools.Page.Locator("#picked")).ToHaveTextAsync("picked=RASK", DevToolsPage.Text);
        await Expect(devTools.Panel.Locator("tbody tr", new() { HasText = "click" }).First).ToBeVisibleAsync(DevToolsPage.Visible);
    }
}
