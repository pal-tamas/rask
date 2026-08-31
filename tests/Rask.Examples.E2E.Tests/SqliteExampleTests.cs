using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

// End-to-end against the SQLite production-pragmas sample: the page reports the live pragma values
// (proving UseRaskSqlite applied them on the connection), and a burst of concurrent writers all commit
// (proving WAL + busy_timeout keep "database is locked" from surfacing under contention).
[Collection(SqliteExampleCollection.Name)]
public sealed class SqliteExampleTests(SqliteExampleAppFixture app, PlaywrightFixture pw) : IAsyncLifetime
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
    public async Task Page_reports_wal_and_foreign_keys_pragmas()
    {
        await _page.GotoAsync("/");

        // The live pragma table renders the values the connection is actually running with.
        var journalRow = _page.Locator("tr:has-text('journal_mode')");
        await Assertions.Expect(journalRow).ToContainTextAsync("wal");

        var foreignKeysRow = _page.Locator("tr:has-text('foreign_keys')");
        await Assertions.Expect(foreignKeysRow).ToContainTextAsync("1");

        var busyTimeoutRow = _page.Locator("tr:has-text('busy_timeout')");
        await Assertions.Expect(busyTimeoutRow).ToContainTextAsync("5000");
    }

    [Fact]
    public async Task Concurrent_writers_all_commit()
    {
        await _page.GotoAsync("/");

        await _page.ClickAsync("button:has-text('concurrent writers')");

        // With WAL + busy_timeout every writer commits — the success alert reports 25 of 25.
        var result = _page.Locator("[role='status']");
        await Assertions.Expect(result).ToBeVisibleAsync();
        await Assertions.Expect(result).ToContainTextAsync("25 of 25 writers committed");
    }

    [Fact]
    public async Task Concurrent_immediate_writers_all_commit()
    {
        await _page.GotoAsync("/");

        await _page.ClickAsync("button:has-text('IMMEDIATE writers')");

        // The BEGIN IMMEDIATE + non-blocking fair-interval retry commits every writer — 25 of 25.
        var result = _page.Locator("[role='status']:has-text('IMMEDIATE')");
        await Assertions.Expect(result).ToBeVisibleAsync();
        await Assertions.Expect(result).ToContainTextAsync("25 of 25 IMMEDIATE writers committed");
    }

    [Fact]
    public async Task Bulk_import_lands_every_row_on_both_paths()
    {
        await _page.GotoAsync("/");

        var result = _page.Locator("#import-result");

        await _page.ClickAsync("#import-tracked");
        await Assertions.Expect(result).ToContainTextAsync("Change tracker:");
        await Assertions.Expect(result).ToContainTextAsync("Rows imported so far: 10,000.");

        // The raw writer has to land the same rows: 10,000 more on top of the tracked import's 10,000.
        // Asserting the running total (rather than each import in isolation) is what would catch a fast
        // path that wrote nothing, or wrote a partial batch, while still reporting a time.
        await _page.ClickAsync("#import-raw");
        await Assertions.Expect(result).ToContainTextAsync("SkipChangeTracking:");
        await Assertions.Expect(result).ToContainTextAsync("Rows imported so far: 20,000.");
    }
}
