using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     <c>Rask.Example.Wasm.Jobs</c> — Rask.Jobs running against a real SQLite database inside the
///     browser, served from a plain static host with nothing behind it.
/// </summary>
/// <remarks>
///     This is the only end-to-end evidence for a chain that unit tests cannot reach: the WASM host
///     actually starting a registered <c>IHostedService</c>, EF Core opening a natively-linked SQLite
///     database in the browser, <c>JobProcessor</c> claiming a row with its lease, the handler writing
///     through, and the database surviving a reload via an IndexedDB snapshot.
/// </remarks>
[Collection(BrowserJobsWasmExampleCollection.Name)]
public sealed class BrowserJobsWasmExampleTests
{
    // The app is untrimmed and ships EF Core plus a native SQLite build, so first boot is much slower
    // than a normal Rask WASM app — and the static host serves it uncompressed.
    private const int BootTimeoutMs = 120_000;

    private readonly BrowserJobsWasmAppFixture _app;
    private readonly PlaywrightFixture _pw;

    public BrowserJobsWasmExampleTests(BrowserJobsWasmAppFixture app, PlaywrightFixture pw)
    {
        _app = app;
        _pw = pw;
    }

    [Fact]
    public async Task A_queued_job_runs_in_the_browser_and_survives_a_reload()
    {
        // One context for the whole test: IndexedDB is per-origin-per-context, and the reload below is
        // only meaningful if the storage the first page wrote is still there for the second.
        var context = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.BaseUrl });
        var page = await context.NewPageAsync();

        try
        {
            await page.GotoAsync("/index.html");

            // Booting at all proves the native SQLite link: the database is opened during startup.
            await Expect(page.Locator("[data-testid=enqueue]"))
                .ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
            await Expect(page.Locator("[data-testid=empty]")).ToBeVisibleAsync();

            await page.Locator("[data-testid=name]").FillAsync("browser");
            await page.Locator("[data-testid=enqueue]").ClickAsync();

            // The click only enqueues; the handler runs later, from the processor's poll loop, and the
            // repaint that shows this row is an out-of-band one with no click behind it.
            await Expect(page.Locator("[data-testid=status]")).ToContainTextAsync("Queued a job");
            await Expect(page.Locator("[data-testid=greetings] li"))
                .ToHaveCountAsync(1, new() { Timeout = 60_000 });
            await Expect(page.Locator("[data-testid=greetings] li").First)
                .ToContainTextAsync("Hello, browser!");

            // The sample snapshots every 2s. Wait past one tick: the interval IS the durability window,
            // because the browser does not wait for the page-hide flush.
            await page.WaitForTimeoutAsync(5_000);
            await page.ReloadAsync();

            // Restored from IndexedDB into a fresh in-memory filesystem — the whole point of the package.
            await Expect(page.Locator("[data-testid=greetings] li"))
                .ToHaveCountAsync(1, new() { Timeout = BootTimeoutMs });
            await Expect(page.Locator("[data-testid=greetings] li").First)
                .ToContainTextAsync("Hello, browser!");
        }
        finally
        {
            await context.CloseAsync();
        }
    }
}
