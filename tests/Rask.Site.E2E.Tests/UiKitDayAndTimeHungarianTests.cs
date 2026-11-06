using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The same pages read by a Hungarian browser, which is what loads Hungarian culture data into a WASM app: the day typed
///     year, month, day with Hungary's full stops, and the seconds a moment keeps.
/// </summary>
public sealed class UiKitDayAndTimeHungarianTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    private static readonly KeyboardTypeOptions Keys = new() { Delay = 60 };

    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    protected override string? BrowserLocale => "hu-HU";

    [Fact]
    public Task A_new_day_keeps_the_seconds_of_a_moment_nobody_retimed() => RunAsync(async () =>
    {
        await OpenUiPageAsync("day-and-time", "Day and time");
        var pair = Page.Locator("[data-testid='ui-day-and-time-seconds'] [data-ui-day-and-time]");
        await pair.ScrollIntoViewIfNeededAsync();

        // Hungarian: year, month, day; the day part alone is retyped.
        await Expect(pair.Locator("[data-ui-date-picker] [data-rask-segments]")).ToContainTextAsync(". ");
        await pair.Locator("[data-ui-date-picker] [data-rask-segment='day']").ClickAsync();
        await Page.Keyboard.TypeAsync("09", Keys);
        await Page.Locator("[data-testid='ui-day-and-time-seconds'] button[type='submit']").ClickAsync();

        await Expect(Page.Locator("#udt-seconds-said")).ToHaveTextAsync("Saved 2026-11-09 14:35:42.", new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
    });

    [Fact]
    public Task A_typed_date_picker_takes_a_hungarian_day_key_by_key() => RunAsync(async () =>
    {
        await OpenUiPageAsync("date-picker", "Date picker");
        var picker = Page.Locator("[data-testid='ui-date-picker-typed'] [data-ui-date-picker]");
        await picker.ScrollIntoViewIfNeededAsync();

        await picker.Locator("[data-rask-segment='year']").ClickAsync();
        await Page.Keyboard.TypeAsync("20261106", Keys);

        await Expect(Page.Locator("[data-testid='ui-date-picker-typed-state']")).ToHaveTextAsync("Chosen: 2026-11-06",
            new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await Expect(picker.Locator("[data-rask-segments]")).ToHaveTextAsync(". . .");
    });
}
