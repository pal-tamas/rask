using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The data grid in a browser: the sorting and selection a static render cannot show, and the card
///     layout below <c>sm</c> that no markup assertion can prove — the utilities are in the HTML either
///     way, and only a narrow viewport says whether the sheet actually applies them.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitDataGridTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task EveryGridRendersWithARealSize() => RunAsync(async () =>
    {
        await OpenAsync();

        foreach (var id in new[]
                 {
                     "ui-grid-basic", "ui-grid-selection", "ui-grid-grouped", "ui-grid-controlled",
                     "ui-grid-console",
                 })
        {
            var node = Page.Locator($"[data-testid='{id}']");
            await Expect(node).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            var box = await node.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(
                box!.Height > 0,
                $"{id} rendered with zero height, which is what an unstyled kit component looks like.");
        }
    });

    [Fact]
    public Task ClickingAHeaderReordersTheRows() => RunAsync(async () =>
    {
        await OpenAsync();

        var grid = Page.Locator("[data-testid='ui-grid-basic']");
        var firstCell = grid.Locator("tbody tr td").First;

        await Expect(firstCell).ToHaveTextAsync("Rask.Core");

        await grid.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Package" }).ClickAsync();

        // Alphabetical now, which Rask.Cli wins.
        await Expect(firstCell).ToHaveTextAsync("Rask.Cli");
        await Expect(grid.Locator("th[aria-sort='ascending']")).ToBeVisibleAsync();
    });

    [Fact]
    public Task TickingARowReportsItsKeyToThePage() => RunAsync(async () =>
    {
        await OpenAsync();

        var grid = Page.Locator("[data-testid='ui-grid-selection']");
        var state = Page.Locator("[data-testid='ui-grid-selection-state']");

        await Expect(state).ToContainTextAsync("Nothing selected");

        await grid.Locator("tbody input[type='checkbox']").First.CheckAsync();

        await Expect(state).ToContainTextAsync("Selected ids: 1");
    });

    [Fact]
    public Task SelectAllTicksEveryRowItCanName() => RunAsync(async () =>
    {
        await OpenAsync();

        var grid = Page.Locator("[data-testid='ui-grid-selection']");

        await grid.GetByLabel("Select all rows on this page").CheckAsync();

        // The grid is unpaged here, so "this page" is all seven.
        await Expect(Page.Locator("[data-testid='ui-grid-selection-state']"))
            .ToContainTextAsync("1, 2, 3, 4, 5, 6, 7");
    });

    [Fact]
    public Task GroupingFromAHeaderBandsTheRows() => RunAsync(async () =>
    {
        await OpenAsync();

        var grid = Page.Locator("[data-testid='ui-grid-grouped']");

        await grid.GetByLabel("Group by Channel").ClickAsync();

        await Expect(grid.GetByText("Channel: stable (3)")).ToBeVisibleAsync();
        await Expect(grid.GetByText("Channel: preview (3)")).ToBeVisibleAsync();
    });

    [Fact]
    public Task TheControlledGridReportsItsOwnSortAndPage() => RunAsync(async () =>
    {
        await OpenAsync();

        var grid = Page.Locator("[data-testid='ui-grid-controlled']");
        var state = Page.Locator("[data-testid='ui-grid-controlled-state']");

        await Expect(state).ToContainTextAsync("Page 1");
        await Expect(state).ToContainTextAsync("downloads descending");

        await grid.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "2", Exact = true })
            .ClickAsync();

        await Expect(state).ToContainTextAsync("Page 2");
    });

    [Fact]
    public Task OnAPhoneTheTableStacksIntoLabelledLines() => RunAsync(async () =>
    {
        await OpenAsync();

        var grid = Page.Locator("[data-testid='ui-grid-basic']");
        var head = grid.Locator("thead").First;
        var cell = grid.Locator("tbody tr td").First;

        // Wide: a real table, header and all.
        await Expect(head).ToBeVisibleAsync();
        var wide = await cell.EvaluateAsync<string>("el => getComputedStyle(el).display");
        Assert.Equal("table-cell", wide);

        try
        {
            await Page.SetViewportSizeAsync(390, 844);

            // Narrow: the header is gone and every cell is its own labelled line. This is the assertion
            // the unit tests cannot make — the utilities are in the markup at both widths, and only the
            // compiled sheet, loaded beside the app's own, decides whether they do anything. It is what
            // caught the header staying hidden at EVERY width, because the app's sheet defines an
            // unconditional `.hidden` that outranked the kit's `sm:table-header-group`.
            await Expect(head).ToBeHiddenAsync();
            var narrow = await cell.EvaluateAsync<string>("el => getComputedStyle(el).display");
            Assert.Equal("flex", narrow);

            var label = await cell.EvaluateAsync<string>(
                "el => getComputedStyle(el, '::before').content");
            Assert.Contains("Package", label, StringComparison.Ordinal);
        }
        finally
        {
            // The context is per-test, but a viewport left shrunk is the kind of thing that reads as a
            // component bug in whichever test happens to run next.
            await Page.SetViewportSizeAsync(1280, 720);
        }
    });

    [Fact]
    public Task PageLinksMoveTheUrlAndTheBackButtonWalksBack() => RunAsync(async () =>
    {
        await OpenAsync();

        var grid = Page.Locator("[data-testid='ui-grid-console']");
        await Expect(grid.Locator("tbody tr")).ToHaveCountAsync(3);
        await Expect(grid.Locator("[aria-current='page']")).ToHaveTextAsync("1");

        await grid.Locator("a.join-item").Filter(new LocatorFilterOptions { HasText = "2" }).ClickAsync();

        // A link, not a handler: the page is in the address now, and the grid read it back from there.
        await Expect(Page).ToHaveURLAsync(new Regex(@"ui/data-grid/\?page=2$"));
        await Expect(grid.GetByText("Rask.Data", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        await Expect(grid.Locator("[aria-current='page']")).ToHaveTextAsync("2");

        await Page.GoBackAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"ui/data-grid/$"));
        await Expect(grid.Locator("[aria-current='page']")).ToHaveTextAsync("1");
    });

    [Fact]
    public Task ASecondaryColumnWaitsForRoomAndAToneTintsItsRow() => RunAsync(async () =>
    {
        await OpenAsync();

        var grid = Page.Locator("[data-testid='ui-grid-console']");
        var channel = grid.Locator("th").Filter(new LocatorFilterOptions { HasText = "Channel" });
        await Expect(channel).ToBeVisibleAsync();

        // Rask.Core has more than 9000 downloads, so its row carries the success tone.
        var tint = await grid.Locator("tbody tr").First.EvaluateAsync<string>(
            "el => getComputedStyle(el).backgroundColor");
        Assert.NotEqual("rgba(0, 0, 0, 0)", tint);

        try
        {
            // Between sm and md: still a table, but the Channel column has not got its room yet.
            await Page.SetViewportSizeAsync(700, 900);

            await Expect(channel).ToBeHiddenAsync();
            await Expect(grid.Locator("th").Filter(new LocatorFilterOptions { HasText = "Package" }))
                .ToBeVisibleAsync();
        }
        finally
        {
            await Page.SetViewportSizeAsync(1280, 720);
        }
    });

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Data grid");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Data grid",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }
}
