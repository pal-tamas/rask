using Microsoft.Playwright;
using Rask.DevTools.E2E.Tests.Apps;
using Rask.DevTools.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.DevTools.E2E.Tests;

/// <summary>
///     The Tree tab against a real Server page: the page's components with their props, the box a row draws on the page,
///     a pick that goes the other way, and the detail pane with a secret and context.
/// </summary>
[Collection(DevToolsHostCollection.Name)]
public sealed class ServerTreeJourneyTests(PlaywrightFixture playwright)
{
    [Fact]
    public async Task Pointing_at_a_row_boxes_exactly_what_that_component_rendered()
    {
        await using var host = await DevToolsServerHost.StartAsync<BoardApp>();
        await using var context = await playwright.Browser.NewContextAsync();
        var devTools = await OpenTreeAsync(context, host);

        await Row(devTools, "Tag the release").HoverAsync();

        var label = devTools.Page.Locator("rask-devtools .hl-label");
        await Expect(label).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^BoardRow"), DevToolsPage.Text);
        var box = await devTools.Page.Locator("rask-devtools .hl").BoundingBoxAsync();
        var row = await devTools.Page.Locator("#board li", new() { HasText = "Tag the release" }).BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.NotNull(row);
        Assert.InRange(box!.X, row!.X - 1, row.X + 1);
        Assert.InRange(box.Y, row.Y - 1, row.Y + 1);
        Assert.InRange(box.Width, row.Width - 2, row.Width + 2);
        Assert.InRange(box.Height, row.Height - 2, row.Height + 2);
    }

    [Fact]
    public async Task A_pick_selects_the_nearest_component_in_the_tree_and_the_click_never_reaches_the_app()
    {
        await using var host = await DevToolsServerHost.StartAsync<BoardApp>();
        await using var context = await playwright.Browser.NewContextAsync();
        var devTools = await OpenTreeAsync(context, host);

        var button = await devTools.Page.Locator("#add-task").BoundingBoxAsync();
        Assert.NotNull(button);
        await devTools.Panel.GetByRole(AriaRole.Button, new() { Name = "Pick" }).ClickAsync();
        await Expect(devTools.Panel.Locator("[aria-pressed=\"true\"]", new() { HasText = "Pick" })).ToBeVisibleAsync(DevToolsPage.Visible);

        // By the pointer, not the element: while picking, a glass over the page takes every pointer event, which is what
        // keeps the click from the app. The button is the board's own markup, so the board is what the pick selects.
        var (x, y) = (button!.X + (button.Width / 2), button.Y + (button.Height / 2));
        await devTools.Page.Mouse.MoveAsync(x, y, new() { Steps = 4 });
        await Expect(devTools.Page.Locator("rask-devtools .hl-label"))
            .ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^Board\\b"), DevToolsPage.Text);
        await devTools.Page.Mouse.ClickAsync(x, y);

        await Expect(Details(devTools).Locator(".font-semibold").First).ToHaveTextAsync("Board", DevToolsPage.Text);
        await Expect(devTools.Page.Locator("#board li")).ToHaveCountAsync(2);
        await Expect(devTools.Panel.Locator("[aria-pressed=\"false\"]", new() { HasText = "Pick" })).ToBeVisibleAsync(DevToolsPage.Visible);
    }

    [Fact]
    public async Task The_detail_pane_shows_a_components_props_with_a_secret_withheld_and_the_context_it_reads()
    {
        await using var host = await DevToolsServerHost.StartAsync<BoardApp>();
        await using var context = await playwright.Browser.NewContextAsync();
        var devTools = await OpenTreeAsync(context, host);

        await Row(devTools, "DeployCard").ClickAsync();
        await Expect(Details(devTools)).ToContainTextAsync("staging", DevToolsPage.Contains);
        await Expect(Details(devTools)).ToContainTextAsync("••••", DevToolsPage.Contains);
        Assert.DoesNotContain("sk_live", await devTools.Panel.Locator("body").InnerTextAsync(), StringComparison.Ordinal);

        // Context is recorded from the first render a panel watches: one click, then the row's reads name the board.
        await devTools.Page.ClickAsync("#add-task");
        await Row(devTools, "Tag the release").ClickAsync();
        var reads = devTools.Panel.Locator("[data-rask-devtools-reads]");
        await Expect(reads).ToContainTextAsync("Release", DevToolsPage.Contains);
        await reads.GetByRole(AriaRole.Button, new() { Name = "Board" }).ClickAsync();
        await Expect(devTools.Panel.Locator("[data-rask-devtools-provides]")).ToContainTextAsync("Ship 1.4", DevToolsPage.Contains);
    }

    private static async Task<DevToolsPage> OpenTreeAsync(IBrowserContext context, DevToolsServerHost host)
    {
        var devTools = await DevToolsPage.OpenAsync(context, host.BaseUrl + "/");
        await devTools.OpenPanelAsync();
        await devTools.ShowTabAsync("Tree");
        await Expect(devTools.Panel.GetByRole(AriaRole.Tree)).ToBeVisibleAsync(DevToolsPage.Visible);
        return devTools;
    }

    // A tree row by the text on it: a component's type or one of its prop values.
    private static ILocator Row(DevToolsPage devTools, string text) =>
        devTools.Panel.Locator("[role=treeitem]", new() { HasText = text }).Last.Locator(".ui-tree-row").First;

    private static ILocator Details(DevToolsPage devTools) => devTools.Panel.Locator("[data-rask-devtools-details]");
}
