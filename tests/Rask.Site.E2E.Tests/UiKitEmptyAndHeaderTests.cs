using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The UI tab's pages for #1283: <c>Ui.Empty</c> in a table's spanning row and in place of a list, a row that folds out
///     with <c>Ui.TableCell.ColSpan</c>, and <c>Ui.PageHeader</c>, whose actions go under its title on a phone.
/// </summary>
public sealed class UiKitEmptyAndHeaderTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task A_search_that_matches_nothing_leaves_one_status_row_across_the_table() => RunAsync(async () =>
    {
        await OpenUiPageAsync("empty", "Empty state");
        var scope = Page.Locator("[data-testid='ui-empty-row']");
        var rows = scope.Locator("#ue-suppliers tbody tr");
        await scope.ScrollIntoViewIfNeededAsync();
        await Expect(rows).ToHaveCountAsync(3);

        await scope.Locator("input").FillAsync("zz");

        await Expect(rows).ToHaveCountAsync(1, new() { Timeout = 10_000 });
        await Expect(rows.Locator("td")).ToHaveAttributeAsync("colspan", "2");
        await Expect(rows.Locator("td").GetByRole(AriaRole.Status)).ToHaveTextAsync("No supplier matches “zz”.");
        var look = await rows.Locator("[role=status]").EvaluateAsync<string[]>(
            "el => { const c = getComputedStyle(el); return [c.paddingTop, c.paddingBottom, c.textAlign, c.fontSize]; }");
        Assert.Equal(["24px", "24px", "center", "14px"], look);
    });

    [Fact]
    public Task A_list_with_nothing_in_it_says_so_in_place_of_its_table() => RunAsync(async () =>
    {
        await OpenUiPageAsync("empty", "Empty state");
        var scope = Page.Locator("[data-testid='ui-empty-list']");
        await scope.ScrollIntoViewIfNeededAsync();
        await Expect(scope.Locator("#ue-drivers tbody tr")).ToHaveCountAsync(2);

        await scope.GetByRole(AriaRole.Button, new() { Name = "Archive every driver" }).ClickAsync();

        await Expect(scope.Locator("#ue-drivers-empty[role=status]")).ToHaveTextAsync("No drivers yet.", new() { Timeout = 10_000 });
        await Expect(scope.Locator("#ue-drivers")).ToHaveCountAsync(0);
    });

    [Fact]
    public Task A_folded_out_row_is_one_cell_across_every_column() => RunAsync(async () =>
    {
        await OpenUiPageAsync("table", "Table");
        var table = Page.Locator("#utf-pumps");
        var rows = table.Locator("tbody tr");
        await table.ScrollIntoViewIfNeededAsync();
        await Expect(rows).ToHaveCountAsync(3);
        await Expect(rows.Nth(1).Locator("td")).ToHaveAttributeAsync("colspan", "3");

        await rows.Nth(2).GetByRole(AriaRole.Button).ClickAsync();

        await Expect(rows).ToHaveCountAsync(4, new() { Timeout = 10_000 });
        await Expect(rows.Nth(3).Locator("td[colspan='3']")).ToContainTextAsync("Pistol 3");
        var spans = await rows.Nth(3).Locator("td").EvaluateAsync<double[]>("td => [td.getBoundingClientRect().width, td.closest('table').getBoundingClientRect().width]");
        Assert.Equal(spans[1], spans[0], 0.5);
    });

    [Fact]
    public Task A_page_header_keeps_its_actions_at_the_end_and_puts_them_under_the_title_on_a_phone() => RunAsync(async () =>
    {
        await OpenUiPageAsync("page-header", "Page header");
        var header = Page.Locator("[data-testid='ui-page-header'] [data-ui-page-header]");
        var title = header.GetByRole(AriaRole.Heading, new() { Name = "Suppliers" });
        var add = header.GetByRole(AriaRole.Link, new() { Name = "New supplier" });
        await header.ScrollIntoViewIfNeededAsync();

        await Expect(header.Locator("[data-ui-breadcrumbs]")).ToContainTextAsync("Home");
        await Expect(header.Locator("[data-ui-subheading]")).ToHaveTextAsync("Everyone you buy fuel and parts from.");
        var wide = await Boxes(title, add, header);
        await Page.SetViewportSizeAsync(390, 844);
        await Expect(add).ToBeVisibleAsync();
        var phone = await Boxes(title, add, header);

        Assert.True(wide.Add.Y < wide.Title.Y + wide.Title.Height, "the actions are not on the title's row on a desktop");
        Assert.Equal(wide.Header.X + wide.Header.Width, wide.Add.X + wide.Add.Width, 1);
        Assert.True(phone.Add.Y >= phone.Title.Y + phone.Title.Height, "the actions are not under the title on a phone");
        Assert.Equal(phone.Header.X + phone.Header.Width, phone.Add.X + phone.Add.Width, 1);
    });

    private static async Task<(LocatorBoundingBoxResult Title, LocatorBoundingBoxResult Add, LocatorBoundingBoxResult Header)> Boxes(
        ILocator title, ILocator add, ILocator header) =>
        ((await title.BoundingBoxAsync())!, (await add.BoundingBoxAsync())!, (await header.BoundingBoxAsync())!);
}
