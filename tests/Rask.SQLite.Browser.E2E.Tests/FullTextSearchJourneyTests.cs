using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.SQLite.Browser.E2E.Tests;

/// <summary>
///     #1129: <c>HasFullTextSearch</c>, <c>Search</c> and <c>FullText.Highlight</c>/<c>Snippet</c> on a browser SQLite
///     database — FTS5 in the e_sqlite3 a WebAssembly app links, and EF's query rewrite under the WASM runtime.
/// </summary>
public sealed class FullTextSearchJourneyTests(PlaywrightFixture playwright, FullTextFixtureHost host)
    : IClassFixture<PlaywrightFixture>, IClassFixture<FullTextFixtureHost>
{
    // The first WASM boot downloads and starts the runtime, EF Core and SQLite.
    private static readonly LocatorAssertionsToHaveTextOptions Boot = new() { Timeout = 60_000 };
    private static readonly LocatorAssertionsToHaveTextOptions Text = new() { Timeout = 15_000 };
    private static readonly LocatorAssertionsToHaveCountOptions Count = new() { Timeout = 15_000 };

    [Fact]
    public async Task A_search_finds_its_rows_best_first_and_marks_every_matched_term()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#search-sqlite");

        await Expect(page.Locator("#status")).ToHaveTextAsync("searched 'sqlite': 1", Text);
        var hit = page.Locator("#hits .hit");
        await Expect(hit).ToHaveCountAsync(1, Count);
        await Expect(hit.Locator(".title")).ToHaveTextAsync("All about SQLite");
        await Expect(hit.Locator(".title mark")).ToHaveTextAsync("SQLite");
    }

    [Fact]
    public async Task Every_word_must_match_and_the_last_one_matches_as_a_prefix()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#search-prefix");

        await Expect(page.Locator("#status")).ToHaveTextAsync("searched 'offline stor': 1", Text);
        var hit = page.Locator("#hits .hit");
        await Expect(hit.Locator(".title")).ToHaveTextAsync("Offline first");
        // The snippet marks the prefix match inside the body.
        await Expect(hit.Locator(".excerpt mark")).ToHaveTextAsync("storage");
    }

    [Fact]
    public async Task A_word_in_no_row_finds_nothing()
    {
        var page = await OpenAsync();

        await page.ClickAsync("#search-nothing");

        await Expect(page.Locator("#status")).ToHaveTextAsync("searched 'zebra': 0", Text);
        await Expect(page.Locator("#hits .hit")).ToHaveCountAsync(0, Count);
    }

    // A fresh browser context per journey: its own IndexedDB, so each one migrates and seeds a new database.
    private async Task<IPage> OpenAsync()
    {
        var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/");
        await Expect(page.Locator("#status")).ToHaveTextAsync("ready", Boot);
        return page;
    }
}
