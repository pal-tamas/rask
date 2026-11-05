using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's list pieces on the UI tab (#1298): <c>Ui.RowActions</c>' pencil and bin, <c>Ui.FormButtons</c>' Delete,
///     Cancel and Save, and a table whose headings name widths — each asking in the layout's <c>Ui.Confirm</c>, never the
///     browser's own dialog.
/// </summary>
public sealed class UiKitListActionsTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    private const string Dialog = "#ui-confirm";
    private const string Yes = Dialog + " [data-rask-confirm-dialog=go]";
    private const string No = Dialog + " .flex button:not([data-rask-confirm-dialog])";

    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task The_bin_asks_first_and_deletes_only_on_yes_and_a_row_with_orders_has_none() => RunAsync(async () =>
    {
        var browserDialogs = Count();
        await OpenUiPageAsync("row-actions", "Row actions");
        var scope = Page.Locator("[data-testid='ui-row-actions']");
        var rows = scope.Locator("#ura-suppliers tbody tr");
        var bothman = rows.Filter(new() { HasText = "Bothman" });
        await scope.ScrollIntoViewIfNeededAsync();

        await Expect(rows).ToHaveCountAsync(3);
        await Expect(rows.Filter(new() { HasText = "Aminoff" }).Locator("button[data-rask-confirm]")).ToHaveCountAsync(0);
        await Expect(bothman.Locator("a[data-ui-button]")).ToHaveAttributeAsync("href", new Regex("/ui/form-buttons/?$"));
        await bothman.Locator("button[data-rask-confirm]").ClickAsync();
        await Expect(Page.Locator(Dialog + " [data-rask-confirm-dialog=heading]")).ToHaveTextAsync("Delete supplier");
        await Expect(Page.Locator(Dialog + " [data-rask-confirm-dialog=message]")).ToHaveTextAsync("Delete Bothman & Sons?");
        await Page.ClickAsync(No);
        await Expect(Page.Locator(Dialog)).ToBeHiddenAsync();
        await Expect(rows).ToHaveCountAsync(3);
        await bothman.Locator("button[data-rask-confirm]").ClickAsync();
        await Page.ClickAsync(Yes);

        await Expect(rows).ToHaveCountAsync(2, new() { Timeout = 10_000 });
        await Expect(bothman).ToHaveCountAsync(0);
        Assert.Empty(browserDialogs);
    });

    [Fact]
    public Task The_form_footer_saves_goes_back_to_the_list_and_deletes_only_on_yes() => RunAsync(async () =>
    {
        var browserDialogs = Count();
        await OpenUiPageAsync("form-buttons", "Form buttons");
        var footer = Page.Locator("[data-testid='ui-form-buttons'] [data-ui-form-buttons]");
        var said = Page.Locator("#ufb-said");
        await footer.ScrollIntoViewIfNeededAsync();

        await Expect(footer.GetByRole(AriaRole.Link, new() { Name = "Cancel" })).ToHaveAttributeAsync("href", new Regex("/ui/row-actions/?$"));
        await footer.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(said).ToHaveTextAsync("Saved Aminoff Freight, Oslo.", new() { Timeout = 10_000 });
        await footer.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        await Expect(Page.Locator(Dialog + " [data-rask-confirm-dialog=heading]")).ToHaveTextAsync("Delete supplier");
        await Page.ClickAsync(No);
        await Expect(said).ToHaveTextAsync("Saved Aminoff Freight, Oslo.");
        await footer.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        await Page.ClickAsync(Yes);

        await Expect(said).ToHaveTextAsync("Deleted Aminoff Freight.", new() { Timeout = 10_000 });
        Assert.Empty(browserDialogs);
    });

    [Fact]
    public Task A_table_of_widths_holds_its_columns_and_scrolls_inside_its_box_on_a_phone() => RunAsync(async () =>
    {
        await OpenUiPageAsync("table", "Table");
        var table = Page.Locator("#ui-deliveries");
        var cut = table.Locator("tbody tr").First.Locator("td").First;
        await table.ScrollIntoViewIfNeededAsync();

        // Rest 12 + Code 8 + Day 7 + Number 7 + Flag 5 + Actions 6 = 45rem.
        var least = await table.EvaluateAsync<string>("el => getComputedStyle(el).minWidth");
        var widths = await table.Locator("thead th").EvaluateAllAsync<double[]>("ths => ths.map(th => th.getBoundingClientRect().width)");
        var title = await cut.GetAttributeAsync("title");
        var truncated = await cut.EvaluateAsync<bool>("td => td.scrollWidth > td.clientWidth");
        await Page.SetViewportSizeAsync(390, 844);
        await Expect(table).ToBeVisibleAsync();
        var page = await Page.EvaluateAsync<double[]>("() => [document.documentElement.scrollWidth, document.documentElement.clientWidth]");
        var frame = await table.EvaluateAsync<double[]>("el => [el.parentElement.scrollWidth, el.parentElement.clientWidth]");

        Assert.Equal("720px", least);
        Assert.Equal(new double[] { 128, 112, 112, 80, 96 }, widths.Skip(1).Select(width => Math.Round(width)).ToArray());
        Assert.StartsWith("Aminoff Freight & Haulage, Nordic Fuel Division", title, StringComparison.Ordinal);
        Assert.True(truncated, "the long supplier name is not cut short");
        Assert.True(page[0] <= page[1], $"the page scrolls sideways on a phone: {page[0]} > {page[1]}");
        Assert.True(frame[0] > frame[1], "the table does not scroll inside its box on a phone");
    });

    // Every browser dialog that appears: with the kit's dialog on the page, one appearing is the bug.
    private List<string> Count()
    {
        List<string> asked = [];
        Page.Dialog += (_, dialog) =>
        {
            asked.Add(dialog.Type + ":" + dialog.Message);
            _ = dialog.DismissAsync();
        };
        return asked;
    }
}
