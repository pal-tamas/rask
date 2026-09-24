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

        // The kit's theme scope survives the first render's morph of <html>: without it every kit colour is absent,
        // which leaves the layout intact and so is invisible to every other assertion here.
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-rask-ui", "");
        var primary = await page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('#add-note')).backgroundColor");
        Assert.NotEqual("rgba(0, 0, 0, 0)", primary);

        await context.CloseAsync();
    }

    [Fact]
    public async Task It_wears_the_theme_the_reader_picked_on_the_site()
    {
        // Same origin as rask.sh, so the demo reads the key the site's theme picker writes.
        var context = await playwright.Browser.NewContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('rask-theme', 'dark')");
        var page = await context.NewPageAsync();
        await OpenAsync(page);

        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");

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
