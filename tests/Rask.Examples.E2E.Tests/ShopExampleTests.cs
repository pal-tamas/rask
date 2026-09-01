using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

/// <summary>
/// Drives <c>samples/Rask.Example.Shop</c> in a browser — the proof that every One Person Framework
/// battery works <b>together</b>, in a running app, rather than passing in isolation.
/// </summary>
/// <remarks>
/// Everything here asserts a pillar <i>ran</i>, not that a row was written. That distinction is the whole
/// point: an outbox row exists whether or not delivery works, a job row exists whether or not the handler
/// ran, and a queued mail row exists whether or not anything was ever sent.
/// </remarks>
[Collection(ShopExampleCollection.Name)]
public sealed class ShopExampleTests(ShopExampleAppFixture app, PlaywrightFixture pw) : IAsyncLifetime
{

    // "processed/total" where the two are equal and non-zero. The tests share one app instance, so counts
    // accumulate across them — an absolute "1/1" would make each test depend on the order the others ran in.
    private static readonly Regex AllProcessed = new(@"^([1-9]\d*)/\1$");

    private IBrowserContext _context = null!;
    private IPage _page = null!;

    public async Task InitializeAsync()
    {
        _context = await pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = app.BaseUrl });
        _page = await _context.NewPageAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task The_app_starts_with_every_pillar_registered()
    {
        // A pillar's processor is a hosted service, and a faulted BackgroundService stops the host by
        // default — so "it answered at all" already rules out a missing table or a bad registration.
        var response = await _page.GotoAsync("/health");
        Assert.NotNull(response);
        Assert.True(response!.Ok, $"/health returned {response.Status}.");

        // …and nothing tripped over a schema that wasn't there.
        Assert.DoesNotContain("no such table", app.ServerLog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_ops_page_reports_the_production_sqlite_pragmas()
    {
        // UseRaskSqlite is a drop-in for UseSqlite whose whole value is these two being set on every
        // connection. Reading them back from the live app is the only assertion that proves it.
        await _page.GotoAsync("/ops");

        await Assertions.Expect(_page.Locator("#ops-journal-mode")).ToHaveTextAsync("wal");
        await Assertions.Expect(_page.Locator("#ops-foreign-keys")).ToHaveTextAsync("1");
    }

    [Fact]
    public async Task Snapshots_are_written_to_disk()
    {
        // Rask.SQLite.Snapshots takes one at startup, through SQLite's Online Backup API.
        var snapshot = await WaitForFileAsync(app.SnapshotDirectory, "*.db", TimeSpan.FromSeconds(30));
        Assert.NotNull(snapshot);

        await _page.GotoAsync("/ops");
        await Assertions.Expect(_page.Locator("#ops-snapshots")).Not.ToHaveTextAsync("0");
    }

    [Fact]
    public async Task Placing_an_order_relays_through_the_outbox_and_sends_the_confirmation()
    {
        // The chain this sample exists to demonstrate:
        //   Rask.Data raises the event -> Rask.Outbox commits it in the SAME transaction ->
        //   the processor relays it via Rask.Cqrs -> the handler queues Rask.Mail and schedules Rask.Jobs.
        await SignInAsync();

        // Navigate through the app rather than deep-linking: clicking the link is itself proof the live
        // session is connected, so the submit below can't race the client's first connection.
        await _page.GotoAsync("/orders");
        await _page.ClickAsync("a:has-text('New Order')");
        await Assertions.Expect(_page.Locator("#customer")).ToBeVisibleAsync();

        await _page.FillAsync("#customer", "Ada Lovelace");
        // Fractional on purpose: a decimal Input now emits step="any", without which the browser's own
        // constraint validation rejects this and never fires submit — silently, with nothing thrown and no
        // validation message. This line is the regression proof for that fix.
        await _page.FillAsync("#total", "42.50");
        await _page.ClickAsync("button[type=submit]");

        // The handler navigates to the list on success. This is a client-side route change, so there is no
        // load event to wait on — poll the URL instead. Failing here means the command never ran, which is
        // a far clearer signal than a counter stuck at 0/0.
        await Assertions.Expect(_page).ToHaveURLAsync(new Regex("/orders$"));
        await Assertions.Expect(_page.Locator("tr:has-text('Ada Lovelace')")).ToBeVisibleAsync();

        await _page.GotoAsync("/ops");

        // Delivered — and delivered cleanly. A key miss or a broken handler doesn't throw; it records an
        // error and retries, so asserting only that it was processed would miss exactly that failure.
        await Assertions.Expect(_page.Locator("#ops-outbox-processed"))
            .ToHaveTextAsync(AllProcessed, new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Assertions.Expect(_page.Locator("#ops-outbox-failed")).ToHaveTextAsync("0");

        // The handler really ran: it queued mail and scheduled a job, neither of which exists otherwise.
        await Assertions.Expect(_page.Locator("#ops-mail-sent"))
            .ToHaveTextAsync(AllProcessed, new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Assertions.Expect(_page.Locator("#ops-jobs-processed"))
            .ToHaveTextAsync(AllProcessed, new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // …and the mail was actually delivered, not just marked sent. The body is a Rask component,
        // rendered to HTML on send.
        var eml = await WaitForEmlContentAsync(app.MailPickupDirectory, TimeSpan.FromSeconds(30));
        Assert.Contains("Order confirmed: Ada Lovelace", eml, StringComparison.Ordinal);
        Assert.Contains("Thanks for your order", eml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_job_enqueued_from_the_ui_is_picked_up_and_processed()
    {
        await SignInAsync();
        await _page.GotoAsync("/ops");

        await _page.ClickAsync("#ops-enqueue-job");
        await Assertions.Expect(_page.Locator("#ops-message")).ToBeVisibleAsync();

        // The page polls once a second; the processor's default poll is 5s. Waiting on the *processed*
        // count (not merely a row appearing) is what proves the handler ran.
        await Assertions.Expect(_page.Locator("#ops-jobs-processed"))
            .ToHaveTextAsync(AllProcessed, new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
    }

    [Fact]
    public async Task The_second_read_of_a_cached_value_is_served_from_cache()
    {
        await SignInAsync();
        await _page.GotoAsync("/ops");

        await _page.ClickAsync("#ops-cache-load");
        await Assertions.Expect(_page.Locator("#ops-cache-source")).ToHaveTextAsync("Computed fresh");
        var first = await _page.Locator("#ops-cache-value").TextContentAsync();

        await _page.ClickAsync("#ops-cache-load");
        await Assertions.Expect(_page.Locator("#ops-cache-source")).ToHaveTextAsync("Served from cache");

        // Same value, not just a cache hit — that is what proves the stored entry was reused.
        Assert.Equal(first, await _page.Locator("#ops-cache-value").TextContentAsync());

        await _page.ClickAsync("#ops-cache-clear");
        await Assertions.Expect(_page.Locator("#ops-cache-entries")).ToHaveTextAsync("0");
    }

    [Fact]
    public async Task Deleting_a_product_hides_it_without_losing_it()
    {
        // Soft delete, not a disappearing row: the record stays, behind Rask.Data's global query filter.
        // The generated page's "Show deleted" view runs the same query with IgnoreQueryFilters, which is
        // what makes the difference between "hidden" and "gone" observable from the browser.
        await SignInAsync();
        await _page.GotoAsync("/products");

        var rows = _page.Locator("table tbody tr");
        var before = await rows.CountAsync();
        Assert.True(before > 0, "The seed should have produced some products.");

        _page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await rows.First.GetByText("Delete").ClickAsync();
        await Assertions.Expect(rows).ToHaveCountAsync(before - 1);

        await _page.GetByText("Show deleted").ClickAsync();
        await Assertions.Expect(rows).ToHaveCountAsync(before);

        // The row that came back offers Restore rather than Edit — it is deleted, not merely filtered out
        // of one view.
        await Assertions.Expect(_page.GetByText("Restore")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Push_endpoints_answer_even_with_no_vapid_keys_configured()
    {
        // The fixture deliberately configures no key pair. The app must still start and the endpoint must
        // still answer — a scaffolded app has to run before you have generated any keys.
        var response = await _page.APIRequest.GetAsync($"{app.BaseUrl}/_push/key");

        Assert.True(response.Ok, $"/_push/key returned {response.Status}.");
        Assert.Contains("publicKey", await response.TextAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_authorized_page_redirects_an_anonymous_deep_link_to_login()
    {
        // [Authorize] on the page turns a full anonymous GET into a 302, before any component renders.
        await _page.GotoAsync("/members");

        Assert.Contains("/login", _page.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_built_in_dashboard_is_gated_on_the_operator_policy()
    {
        // The whole point of the fail-closed design, end to end: /_rask is behind the RaskDashboard policy,
        // so an anonymous deep link never reaches a page that shows job payloads and stored email bodies.
        await _page.GotoAsync("/_rask");
        Assert.Contains("/login", _page.Url, StringComparison.Ordinal);

        await SignInAsync();
        await _page.GotoAsync("/_rask");

        // Signed in, the operator sees the console's sections.
        Assert.DoesNotContain("/login", _page.Url, StringComparison.Ordinal);
        await Assertions.Expect(_page.Locator("nav a", new() { HasTextString = "Queues" })).ToBeVisibleAsync();

        // The individual queues used to be one top-level tab each; they are one "Queues" tab plus a
        // breadcrumb switcher now, so the claim that this app registered Jobs AND Outbox moves to that
        // switcher's options. Asserted by count, not visibility: an <option> is never "visible".
        await _page.ClickAsync("nav a:has-text('Queues')");
        await Assertions.Expect(_page.Locator("header select option:has-text('Jobs')")).ToHaveCountAsync(1);
        await Assertions.Expect(_page.Locator("header select option:has-text('Outbox')")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task The_dashboard_fits_a_phone_without_scrolling_sideways()
    {
        // The console is built mobile-first, and the one failure that makes it unusable rather than merely
        // ugly is a page wider than the viewport — a stack trace, a long queue name or a table that forgot
        // to drop a column will all do it, and none of them fail any other assertion here.
        await SignInAsync();
        await _page.SetViewportSizeAsync(360, 780);

        foreach (var path in new[] { "/_rask", "/_rask/queues/jobs", "/_rask/cache", "/_rask/logs", "/_rask/system" })
        {
            await _page.GotoAsync(path);
            await Assertions.Expect(_page.Locator("nav a", new() { HasTextString = "Overview" })).ToBeVisibleAsync();

            // scrollWidth over clientWidth on the document: this is the actual definition of "the page
            // scrolls sideways", rather than a proxy for it.
            var overflows = await _page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
            Assert.False(overflows, $"{path} scrolls horizontally at 360px wide.");

            // And the tables separately, because they carry their own overflow-x as a backstop — so a
            // column that failed to collapse hides its content behind an internal scrollbar while the
            // document check above stays perfectly green. That is exactly how this shipped once: a request
            // id in a log scope pushed the table wide and nothing failed.
            var scrollers = await _page.EvaluateAsync<string[]>(
                """
                () => [...document.querySelectorAll('table')]
                    .filter(t => t.scrollWidth > t.parentElement.clientWidth + 1)
                    .map(t => t.parentElement.className)
                """);
            Assert.True(scrollers.Length == 0, $"{path} has a table wider than the phone: {string.Join(" | ", scrollers)}");
        }
    }

    [Fact]
    public async Task The_stored_log_is_searchable_from_the_dashboard()
    {
        // The one thing no unit test can show: a real app's real log lines, written by the real
        // ILoggerProvider through the real background writer, coming back out of the store's own file.
        await SignInAsync();
        await _page.GotoAsync("/_rask/logs?view=history");

        // History exists at all only because Rask.Logging is registered — without it the page is the
        // in-memory tail and offers no such mode.
        await Assertions.Expect(_page.Locator("nav a", new() { HasTextString = "History" }))
            .ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText(new Regex("stored entries, kept across restarts")))
            .ToBeVisibleAsync();

        // The app logs its start-up lines, and the writer flushes on an interval — so this is also the
        // assertion that the buffer really does reach disk. It only holds because the sample keeps
        // Microsoft.Hosting.Lifetime at Information: the logging pipeline filters before the store ever
        // sees an entry, which is why an app on `"Default": "Warning"` alone stores nothing at all.
        await Assertions.Expect(_page.Locator("table tbody tr").First).ToBeVisibleAsync(
            new() { Timeout = 15_000 });
        // `.First`, because the claim is "a start-up line reached the store", not "exactly one did". The
        // store is a file in the sample's publish directory and the fixture reuses that directory, so the
        // SECOND run of this suite finds two — and a strict locator then fails with a match-count error
        // that reads like a UI bug. Which made the gate pass once and fail on the re-run, i.e. exactly
        // when someone re-runs it after fixing something else.
        await Assertions.Expect(_page.GetByText("Application started").First).ToBeVisibleAsync();

        // The live tail is still there beside it, reading nothing from disk.
        await _page.ClickAsync("nav a:has-text('Live')");
        await Assertions.Expect(_page.GetByText(new Regex("in memory only"))).ToBeVisibleAsync();
    }

    private async Task SignInAsync()
    {
        await _page.GotoAsync("/login");
        await _page.FillAsync("#username", "alice");
        await _page.FillAsync("#password", "password");
        await _page.ClickAsync("button[type=submit]");
        await Assertions.Expect(_page).Not.ToHaveURLAsync(new Regex("/login"));

        // …and then wait for the AUTHORIZED CONTENT, not just for the URL to change (#692). On the Server
        // host, signing in cannot set a cookie on a response that has already been sent — the live session
        // issues an AuthInstruction and the CLIENT performs the navigation that commits it. So the URL
        // moves off /login before the cookie exists, and the next `GotoAsync` races that commit: the
        // caller lands on an authorized page and is bounced straight back to /login, with sign-in
        // apparently complete. Reproduced 2 runs in 3 in isolation.
        //
        // MemberContent renders only for an authorized principal, so its Sign-out button appearing is
        // evidence the cookie round-trip finished — which is what every caller of this actually needs.
        await Assertions.Expect(_page.Locator("button:has-text('Sign out')"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
    }

    private static async Task<string?> WaitForFileAsync(string directory, string pattern, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var match = Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, pattern).FirstOrDefault()
                : null;

            if (match is not null)
            {
                return match;
            }

            await Task.Delay(250);
        }

        return null;
    }

    // Polls for an .eml and reads it once it is fully written. The pickup sender creates the file with
    // FileShare.None while it streams the body, so a read mid-write throws IOException — tolerate that and
    // retry until the write completes (or the timeout elapses).
    private static async Task<string> WaitForEmlContentAsync(string directory, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var eml = Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.eml").FirstOrDefault()
                : null;

            if (eml is not null)
            {
                try
                {
                    return await File.ReadAllTextAsync(eml);
                }
                catch (IOException)
                {
                    // Still being written — fall through and retry.
                }
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"No readable .eml appeared in {directory} within {timeout}.");
    }
}
