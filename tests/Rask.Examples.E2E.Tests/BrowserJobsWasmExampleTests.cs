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

    /// <summary>
    ///     The <c>pagehide</c> drain, and specifically its back/forward-cache guard.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Observed through the snapshot loop rather than through the drain directly, because a stopped
    ///         hosted service has no visible output of its own. The sample snapshots every 2s and logs each
    ///         one, so "did the services stop?" becomes "did the log go quiet?" — which is deterministic,
    ///         unlike asserting on the final flush itself (the browser does not wait for a <c>pagehide</c>
    ///         handler, so that would be genuinely flaky).
    ///     </para>
    ///     <para>
    ///         The <c>persisted: true</c> half is the point. A bfcache suspend can be restored with its
    ///         services still needed, so draining there would leave a resumed page with dead background
    ///         work and nothing to indicate why — the exact silent failure this whole change set exists to
    ///         remove.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task Pagehide_drains_hosted_services_but_not_for_a_bfcache_suspend()
    {
        var context = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.BaseUrl });
        var page = await context.NewPageAsync();

        var snapshots = 0;
        page.Console += (_, message) =>
        {
            if (message.Text.Contains("Created SQLite snapshot", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref snapshots);
            }
        };

        try
        {
            await page.GotoAsync("/index.html");
            await Expect(page.Locator("[data-testid=enqueue]"))
                .ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

            // A bfcache suspend: the page can come back with its services still needed, so the loop must
            // keep running.
            await page.EvaluateAsync(
                "() => window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }))");

            var beforeSuspend = Volatile.Read(ref snapshots);
            await page.WaitForTimeoutAsync(6_000);
            var afterSuspend = Volatile.Read(ref snapshots);

            Assert.True(
                afterSuspend > beforeSuspend,
                $"a bfcache pagehide stopped the snapshot loop: {beforeSuspend} -> {afterSuspend} over ~6s "
                + "with a 2s interval. The event.persisted guard in rask.wasm.js is not holding.");

            // A real teardown: the services drain, so the loop stops.
            await page.EvaluateAsync(
                "() => window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: false }))");

            // One interval of slack: a tick already in flight when the drain landed may still log.
            await page.WaitForTimeoutAsync(3_000);
            var afterTeardown = Volatile.Read(ref snapshots);
            await page.WaitForTimeoutAsync(6_000);

            Assert.Equal(afterTeardown, Volatile.Read(ref snapshots));
        }
        finally
        {
            await context.CloseAsync();
        }
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
