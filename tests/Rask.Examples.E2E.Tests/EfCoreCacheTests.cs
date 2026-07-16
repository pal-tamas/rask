using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

// End-to-end for the Rask.Cache slice against the EF Core + SQLite sample: the first load runs the expensive
// factory ("Computed fresh") and stores a CacheEntry row; a second load is served from that row ("Served from
// cache") with the same value; clearing the entry makes the next load recompute.
[Collection(EfCoreExampleCollection.Name)]
public sealed class EfCoreCacheTests(EfCoreExampleAppFixture app, PlaywrightFixture pw) : IAsyncLifetime
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
    public async Task Second_load_is_served_from_cache_and_clearing_forces_a_recompute()
    {
        await _page.GotoAsync("/cache");

        // First load: a miss, so the factory runs.
        await _page.ClickAsync("#cache-load");
        await Assertions.Expect(_page.Locator("#cache-result")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("#cache-source")).ToHaveTextAsync("Computed fresh");
        var firstValue = await _page.Locator("#cache-value").InnerTextAsync();

        // Second load: served from the database-backed cache, same value, no recompute.
        await _page.ClickAsync("#cache-load");
        await Assertions.Expect(_page.Locator("#cache-source")).ToHaveTextAsync("Served from cache");
        await Assertions.Expect(_page.Locator("#cache-value")).ToHaveTextAsync(firstValue);

        // Clear the entry, then load again: back to a fresh computation.
        await _page.ClickAsync("#cache-clear");
        await Assertions.Expect(_page.Locator("#cache-result")).Not.ToBeVisibleAsync();
        await _page.ClickAsync("#cache-load");
        await Assertions.Expect(_page.Locator("#cache-source")).ToHaveTextAsync("Computed fresh");
    }
}
