using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.DataDemo.E2E.Tests;

/// <summary>
///     The rask.sh data demo end to end, the way a reader uses it: the mouse into a field, the keyboard to type, the
///     mouse on the button. Everything under test runs in the tab — EF Core writing a Rask.Data aggregate to SQLite,
///     the save refreshing a Rask.Query list, FTS5 ranking and marking a match, the database surviving a reload.
/// </summary>
public sealed class NotesJourneyTests(PlaywrightFixture playwright, DataDemoHost host)
    : IClassFixture<PlaywrightFixture>, IClassFixture<DataDemoHost>
{
    // The first boot downloads and starts the runtime, EF Core and SQLite, then builds and seeds the schema.
    private static readonly LocatorAssertionsToHaveCountOptions Boot = new() { Timeout = 60_000 };
    private static readonly LocatorAssertionsToHaveTextOptions Text = new() { Timeout = 15_000 };
    private static readonly LocatorAssertionsToHaveCountOptions Count = new() { Timeout = 15_000 };

    [Fact]
    public async Task A_note_added_with_the_mouse_is_listed_found_highlighted_and_still_there_after_a_reload()
    {
        var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenAsync(page);

        // Add a note: click into each field, type, click the button.
        await page.ClickAsync("#note-title");
        await page.Keyboard.TypeAsync("Zebra crossing");
        await page.ClickAsync("#note-body");
        await page.Keyboard.TypeAsync("Stripes on the road tell drivers to stop for walkers.");
        await page.ClickAsync("#add-note");

        // The save refreshed the list with nothing written at the call site: newest first, four rows now.
        var notes = page.Locator("#notes li");
        await Expect(notes).ToHaveCountAsync(4, Count);
        await Expect(notes.First.Locator(".note-title")).ToHaveTextAsync("Zebra crossing", Text);
        // And the form started over.
        await Expect(page.Locator("#note-title")).ToHaveValueAsync("");

        // Search it: ranked by FTS5, the matched word marked in the title and in the snippet.
        await page.ClickAsync("#search");
        await page.Keyboard.TypeAsync("zebra");
        var hit = page.Locator("#hits .hit");
        await Expect(hit).ToHaveCountAsync(1, Count);
        await Expect(hit.Locator(".hit-title")).ToHaveTextAsync("Zebra crossing", Text);
        await Expect(hit.Locator(".hit-title mark")).ToHaveTextAsync("Zebra", Text);
        await Expect(page.Locator("#results-heading")).ToHaveTextAsync("1 match for “zebra”", Text);

        // A prefix of the last word matches too, in the body.
        await page.ClickAsync("#search");
        await page.Keyboard.PressAsync("ControlOrMeta+A");
        await page.Keyboard.TypeAsync("walk");
        // The search follows the box, so "w" and "wa" had results of their own: wait for the last word's before
        // reading the marks, or a strict locator can land on an earlier result list.
        await Expect(page.Locator("#results-heading")).ToHaveTextAsync("1 match for “walk”", Text);
        await Expect(hit.Locator(".note-body mark")).ToHaveTextAsync("walkers", Text);

        // The database is written to IndexedDB every two seconds; wait out one tick, then reload.
        await page.WaitForTimeoutAsync(3_000);
        await page.ReloadAsync();
        await Expect(page.Locator("#notes li")).ToHaveCountAsync(4, Boot);
        await Expect(page.Locator("#notes li .note-title").First).ToHaveTextAsync("Zebra crossing", Text);

        await context.CloseAsync();
    }

    [Fact]
    public async Task The_newest_note_is_renamed_through_a_form_over_its_operation_that_says_what_the_domain_refuses_as_it_is_typed()
    {
        var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenAsync(page);
        var newest = page.Locator("#notes li .note-title").First;
        var title = page.Locator("#f-title");
        var seeded = await newest.InnerTextAsync();

        // Form.For(Note.Rename, id): the field is Rename's parameter, filled from the note it renames.
        await Expect(title).ToHaveValueAsync(seeded, new() { Timeout = 15_000 });

        // Emptied and left: the operation's dry run says what Rename's Refuse says, before any save is tried.
        await page.ClickAsync("#f-title");
        await page.Keyboard.PressAsync("ControlOrMeta+A");
        await page.Keyboard.PressAsync("Backspace");
        await page.Keyboard.PressAsync("Tab");
        await Expect(page.Locator("#f-title-error")).ToContainTextAsync("A note needs a title.", Text);

        // Filled and submitted: saved through Note.Rename(id, version, title), and the list follows.
        await page.ClickAsync("#f-title");
        await page.Keyboard.TypeAsync("Renamed through its operation");
        await page.ClickAsync("#rename-note");
        await Expect(newest).ToHaveTextAsync("Renamed through its operation", Text);

        await context.CloseAsync();
    }

    [Fact]
    public async Task A_note_is_edited_in_a_dialog_over_the_list_and_deleted_unless_its_rules_keep_it()
    {
        var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenAsync(page);
        var welcome = page.Locator("#notes li", new() { HasText = "Welcome to Rask" });

        // The welcome note: its editor opens over the list, and its Delete says why it stays instead of asking.
        await welcome.Locator(".note-edit").ClickAsync();
        await Expect(page.Locator("dialog h2")).ToHaveTextAsync("Edit note", Text);
        await page.ClickAsync("#editor-delete");
        await Expect(page.Locator("#editor-delete-refused")).ToHaveTextAsync("The welcome note stays.", Text);

        // Another note: renamed in the editor, which says "Saved" and closes over the list that follows.
        await page.Keyboard.PressAsync("Escape");
        var offline = page.Locator("#notes li", new() { HasText = "Offline first" });
        await offline.Locator(".note-edit").ClickAsync();
        await page.Locator("dialog[open] input").First.ClickAsync();
        await page.Keyboard.PressAsync("ControlOrMeta+A");
        await page.Keyboard.TypeAsync("Offline, always");
        await page.ClickAsync("#save-note");
        await Expect(page.Locator("#notes li .note-title", new() { HasText = "Offline, always" })).ToHaveCountAsync(1, Count);

        // Deleted: asked first, then gone from the list.
        await page.Locator("#notes li", new() { HasText = "Offline, always" }).Locator(".note-edit").ClickAsync();
        await page.ClickAsync("#editor-delete");
        await page.GetByRole(AriaRole.Button, new() { Name = "Yes" }).ClickAsync();
        await Expect(page.Locator("#notes li")).ToHaveCountAsync(2, Count);

        await context.CloseAsync();
    }

    [Fact]
    public async Task A_search_that_matches_nothing_says_so()
    {
        var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenAsync(page);

        await page.ClickAsync("#search");
        await page.Keyboard.TypeAsync("quokka");

        await Expect(page.Locator("#no-hits")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(page.Locator("#hits .hit")).ToHaveCountAsync(0, Count);

        await context.CloseAsync();
    }

    [Fact]
    public async Task It_fits_a_phone_without_scrolling_sideways()
    {
        var context = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await OpenAsync(page);

        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(overflow <= 0, $"The demo scrolls sideways by {overflow}px at 390px wide.");
        await Expect(page.Locator("#add-note")).ToBeInViewportAsync();

        // The kit's colours reach the page: without them the layout is intact, which every other assertion here sees.
        var primary = await page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('#add-note')).backgroundColor");
        Assert.NotEqual("rgba(0, 0, 0, 0)", primary);

        await context.CloseAsync();
    }

    [Fact]
    public async Task It_is_dark_when_the_reader_chose_dark_on_the_site()
    {
        // Same origin as rask.sh, so the demo reads the key the site's moon writes.
        var context = await playwright.Browser.NewContextAsync(new BrowserNewContextOptions { ColorScheme = ColorScheme.Light });
        await context.AddInitScriptAsync("localStorage.setItem('rask.appearance', 'dark')");
        var page = await context.NewPageAsync();

        await OpenAsync(page);

        await Expect(page.Locator("html")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"(^|\s)dark(\s|$)"));
        var ground = await page.EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor");
        Assert.Equal("rgb(39, 39, 42)", ground);
        await context.CloseAsync();
    }

    // A fresh browser context per journey: its own IndexedDB, so each one starts from the three seeded notes.
    // A boot that never reaches the list fails with the browser console attached, since that is where it says why.
    private async Task OpenAsync(IPage page)
    {
        var console = new List<string>();
        page.Console += (_, message) => console.Add($"[{message.Type}] {message.Text}");
        page.PageError += (_, error) => console.Add($"[pageerror] {error}");

        await page.GotoAsync(host.DemoUrl);
        try
        {
            await Expect(page.Locator("#notes li")).ToHaveCountAsync(3, Boot);
        }
        catch (PlaywrightException error)
        {
            throw new InvalidOperationException(
                "The demo never listed its seeded notes. Browser console:\n  " + string.Join("\n  ", console)
                + "\nBody:\n" + await page.InnerTextAsync("body"), error);
        }
    }
}
