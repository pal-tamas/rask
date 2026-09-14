using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's tree, in a browser — where the keyboard, the indent and the virtualized window are real.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitTreeTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task EveryTreeRendersWithARealSize() => RunAsync(async () =>
    {
        await OpenAsync();

        foreach (var id in new[] { "ui-tree-basic", "ui-tree-controlled", "ui-tree-virtual" })
        {
            var node = Page.Locator($"[data-testid='{id}']");
            await Expect(node).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            var box = await node.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.Height > 0, $"{id} rendered with zero height.");
        }
    });

    [Fact]
    public Task TheArrowKeysMoveTheCursorAndDoNotScrollThePage() => RunAsync(async () =>
    {
        await OpenAsync();

        var tree = Page.Locator("[data-testid='ui-tree-basic'] [role='tree']");
        await tree.FocusAsync();
        var before = await Page.EvaluateAsync<int>("() => window.scrollY");

        await Page.Keyboard.PressAsync("ArrowDown");

        // The cursor is an attribute, so this is what "moved" means for a tree.
        await Expect(tree).Not.ToHaveAttributeAsync("aria-activedescendant", await FirstRowId(tree));
        Assert.Equal(before, await Page.EvaluateAsync<int>("() => window.scrollY"));
    });

    [Fact]
    public Task RightOpensANodeAndLeftClosesIt() => RunAsync(async () =>
    {
        await OpenAsync();

        var tree = Page.Locator("[data-testid='ui-tree-basic'] [role='tree']");
        var firstRow = tree.Locator("[role='treeitem']").First;
        await tree.FocusAsync();

        await Page.Keyboard.PressAsync("ArrowLeft");
        await Expect(firstRow).ToHaveAttributeAsync("aria-expanded", "false");

        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(firstRow).ToHaveAttributeAsync("aria-expanded", "true");
    });

    [Fact]
    public Task SelectingANodeReportsItsKeyToThePage() => RunAsync(async () =>
    {
        await OpenAsync();

        var tree = Page.Locator("[data-testid='ui-tree-controlled'] [role='tree']");
        await tree.Locator(".ui-tree-row").First.ClickAsync();

        await Expect(Page.Locator("[data-testid='ui-tree-controlled-state']"))
            .ToContainTextAsync("selected: src", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task AChildRowIsIndentedUnderItsParent() => RunAsync(async () =>
    {
        await OpenAsync();

        var rows = Page.Locator("[data-testid='ui-tree-basic'] .ui-tree-row");
        var parent = await rows.Nth(0).BoundingBoxAsync();
        var child = await rows.Nth(1).BoundingBoxAsync();

        Assert.NotNull(parent);
        Assert.NotNull(child);
        Assert.True(child!.X > parent!.X, "a child row is not indented past its parent.");
    });

    [Fact]
    public Task VirtualizedRowsAreExactlyOneItemSizeTall() => RunAsync(async () =>
    {
        await OpenAsync();

        var rows = Page.Locator("[data-testid='ui-tree-virtual'] [role='treeitem']");
        await Expect(rows.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var box = await rows.First.BoundingBoxAsync();
        Assert.NotNull(box);
        // The scroll window is computed from this number; a row that is taller drifts out of step with it.
        Assert.Equal(28, (int)Math.Round(box!.Height));
    });

    [Fact]
    public Task OnlyTheVisibleWindowOfALargeTreeIsRendered() => RunAsync(async () =>
    {
        await OpenAsync();

        var rows = Page.Locator("[data-testid='ui-tree-virtual'] [role='treeitem']");
        await Expect(rows.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 100 roots, all of them open: thousands of nodes, a few dozen rows.
        Assert.InRange(await rows.CountAsync(), 1, 60);
    });

    [Fact]
    public Task EndScrollsTheLastNodeIntoView() => RunAsync(async () =>
    {
        await OpenAsync();

        var tree = Page.Locator("[data-testid='ui-tree-virtual'] [role='tree']");
        await tree.FocusAsync();

        await Page.Keyboard.PressAsync("End");

        // The runtime follows the cursor: the row it names is not rendered until the tree scrolls to it.
        await Expect(tree).ToHaveAttributeAsync(
            "aria-activedescendant",
            new Regex(".+"),
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 10_000 });
        Assert.True(
            await tree.EvaluateAsync<double>("el => el.scrollTop") > 0,
            "End did not scroll the virtualized tree.");
    });

    [Fact]
    public Task TypingALetterJumpsToTheMatchingNode() => RunAsync(async () =>
    {
        await OpenAsync();

        var tree = Page.Locator("[data-testid='ui-tree-basic'] [role='tree']");
        await tree.FocusAsync();
        var before = await tree.GetAttributeAsync("aria-activedescendant");

        await Page.Keyboard.PressAsync("r");

        await Expect(tree).Not.ToHaveAttributeAsync("aria-activedescendant", before ?? "");
    });

    [Fact]
    public Task HoveringARowReportsItToThePage() => RunAsync(async () =>
    {
        await OpenAsync();

        await Page.Locator("[data-testid='ui-tree-virtual'] .ui-tree-row").First.HoverAsync();

        await Expect(Page.Locator("[data-testid='ui-tree-hover-state']"))
            .ToContainTextAsync("hovering: branch", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    private static async Task<string> FirstRowId(ILocator tree) =>
        await tree.Locator("[role='treeitem']").First.GetAttributeAsync("id") ?? "";

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Tree");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Tree",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }
}
