using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

// End-to-end CRUD round-trip against the EF Core + SQLite sample: create, edit and delete a product
// through the UI, asserting each step persisted (the list re-reads from SQLite on every navigation).
[Collection(EfCoreExampleCollection.Name)]
public sealed class EfCoreCrudTests(EfCoreExampleAppFixture app, PlaywrightFixture pw) : IAsyncLifetime
{
    private IBrowserContext _ctx = default!;
    private IPage _page = default!;

    public async Task InitializeAsync()
    {
        _ctx = await pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = app.BaseUrl });
        _page = await _ctx.NewPageAsync();
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task Create_Edit_Delete_RoundTripsThroughSqlite()
    {
        await _page.GotoAsync("/products");

        // Seeded data is present on first load.
        await Assertions.Expect(_page.Locator("tr:has-text('Mechanical keyboard')")).ToBeVisibleAsync();

        // CREATE
        await _page.ClickAsync("a:has-text('New product')");
        await _page.FillAsync("#p-name", "E2E gadget");
        await _page.FillAsync("#p-price", "12.34");
        await _page.FillAsync("#p-stock", "7");
        await _page.ClickAsync("button[type=submit]");

        var createdRow = _page.Locator("tr:has-text('E2E gadget')");
        await Assertions.Expect(createdRow).ToBeVisibleAsync();
        await Assertions.Expect(createdRow).ToContainTextAsync("7");

        // EDIT — follow the row's edit link, rename, save.
        await createdRow.Locator("a.btn-outline-secondary").ClickAsync();
        await _page.FillAsync("#p-name", "E2E gizmo");
        await _page.ClickAsync("button[type=submit]");

        await Assertions.Expect(_page.Locator("tr:has-text('E2E gizmo')")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("tr:has-text('E2E gadget')")).ToHaveCountAsync(0);

        // DELETE
        await _page.Locator("tr:has-text('E2E gizmo')").Locator("button.btn-outline-danger").ClickAsync();
        await Assertions.Expect(_page.Locator("tr:has-text('E2E gizmo')")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Create_RejectsInvalidInput_WithInlineMessages()
    {
        await _page.GotoAsync("/products/new");

        // Blank name + non-positive price violate the value-object rules reused by the inline validators.
        await _page.FillAsync("#p-price", "0");
        await _page.ClickAsync("button[type=submit]");

        await Assertions.Expect(_page.Locator("text=Name is required.")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("text=Price must be greater than zero.")).ToBeVisibleAsync();

        // Still on the create page — the invalid submit did not navigate away.
        await Assertions.Expect(_page.Locator("#p-name")).ToBeVisibleAsync();
    }
}
