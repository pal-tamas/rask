using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

// End-to-end against the CRDT sample: three devices, three SQLite databases, one folder-backed bucket
// and nothing else — an edit on one device reaches another after syncing, and concurrent edits to
// different FIELDS of the same todo both survive, which is the claim per-column merging makes.
//
// cr-sqlite's native binary is per-platform and not in this repo, so the journeys run only when
// RASK_CRSQLITE_PATH is set; without it the sample renders a setup card, and that is asserted instead.
// One of the two always runs. Following WasmWatchHotReloadTests, the gate is an early return rather
// than SkippableFact — this project deliberately has no Xunit.SkippableFact reference.
[Collection(CrdtExampleCollection.Name)]
public sealed class CrdtExampleTests(CrdtExampleAppFixture app, PlaywrightFixture pw) : IAsyncLifetime
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
    public async Task Without_the_extension_the_page_says_what_to_download()
    {
        if (CrdtExampleAppFixture.ExtensionAvailable)
        {
            return;
        }

        await _page.GotoAsync("/");

        // A missing extension otherwise surfaces as "no such function: crsql_as_crr", which says
        // nothing about what to do about it.
        var setup = _page.GetByTestId("setup");
        await Assertions.Expect(setup).ToBeVisibleAsync();
        await Assertions.Expect(setup).ToContainTextAsync("RASK_CRSQLITE_PATH");
    }

    [Fact]
    public async Task An_edit_on_one_device_reaches_another()
    {
        if (!CrdtExampleAppFixture.ExtensionAvailable)
        {
            return;
        }

        await _page.GotoAsync("/");

        await _page.GetByTestId("draft-Phone").FillAsync("buy milk");
        await _page.GetByTestId("add-Phone").ClickAsync();

        // Local first: the todo exists on the Phone before anything touches the bucket.
        await Assertions.Expect(_page.GetByTestId("device-Phone")).ToContainTextAsync("buy milk");
        await Assertions.Expect(_page.GetByTestId("device-Laptop")).Not.ToContainTextAsync("buy milk");

        await _page.GetByTestId("sync-all").ClickAsync();

        await Assertions.Expect(_page.GetByTestId("device-Laptop")).ToContainTextAsync("buy milk");
        await Assertions.Expect(_page.GetByTestId("device-Tablet")).ToContainTextAsync("buy milk");
    }

    [Fact]
    public async Task An_offline_device_keeps_working_and_catches_up()
    {
        if (!CrdtExampleAppFixture.ExtensionAvailable)
        {
            return;
        }

        await _page.GotoAsync("/");

        await _page.GetByTestId("link-Tablet").ClickAsync();
        await Assertions.Expect(_page.GetByTestId("link-Tablet")).ToContainTextAsync("Offline");

        await _page.GetByTestId("draft-Tablet").FillAsync("water the plants");
        await _page.GetByTestId("add-Tablet").ClickAsync();

        // The edit is committed to the Tablet's own database; being offline is not an error state.
        await Assertions.Expect(_page.GetByTestId("device-Tablet")).ToContainTextAsync("water the plants");
        await _page.GetByTestId("sync-Tablet").ClickAsync();
        await Assertions.Expect(_page.GetByTestId("status-Tablet")).ToContainTextAsync("offline");
        await Assertions.Expect(_page.GetByTestId("device-Phone")).Not.ToContainTextAsync("water the plants");

        await _page.GetByTestId("link-Tablet").ClickAsync();
        await _page.GetByTestId("sync-all").ClickAsync();

        await Assertions.Expect(_page.GetByTestId("device-Phone")).ToContainTextAsync("water the plants");
    }

    [Fact]
    public async Task Concurrent_edits_to_different_fields_both_survive()
    {
        if (!CrdtExampleAppFixture.ExtensionAvailable)
        {
            return;
        }

        // The claim of the whole stack, driven through a browser: per column, not per row.
        await _page.GotoAsync("/");

        await _page.GetByTestId("draft-Phone").FillAsync("book the ferry");
        await _page.GetByTestId("add-Phone").ClickAsync();
        await _page.GetByTestId("sync-all").ClickAsync();
        await Assertions.Expect(_page.GetByTestId("device-Laptop")).ToContainTextAsync("book the ferry");

        // Both devices go offline, then edit different fields of the same todo.
        await _page.GetByTestId("link-Phone").ClickAsync();
        await _page.GetByTestId("link-Laptop").ClickAsync();

        var phoneRow = _page.GetByTestId("device-Phone").Locator("li:has-text('book the ferry')");
        var laptopRow = _page.GetByTestId("device-Laptop").Locator("li:has-text('book the ferry')");

        await phoneRow.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { NameString = "P1" }).ClickAsync();
        await Assertions.Expect(phoneRow).ToContainTextAsync("P2");

        await laptopRow.Locator("button").Last.ClickAsync();   // the done toggle
        await Assertions.Expect(laptopRow.Locator("s, .text-decoration-line-through")).ToHaveCountAsync(1);

        await _page.GetByTestId("link-Phone").ClickAsync();
        await _page.GetByTestId("link-Laptop").ClickAsync();
        await _page.GetByTestId("sync-all").ClickAsync();

        // Neither edit was chosen over the other: the priority came from the Phone, the done flag from
        // the Laptop, and both devices now show both.
        foreach (var device in new[] { "Phone", "Laptop" })
        {
            var row = _page.GetByTestId($"device-{device}").Locator("li:has-text('book the ferry')");
            await Assertions.Expect(row).ToContainTextAsync("P2");
            await Assertions.Expect(row.Locator(".text-decoration-line-through")).ToHaveCountAsync(1);
        }
    }
}
