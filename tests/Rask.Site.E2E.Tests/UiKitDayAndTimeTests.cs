using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     <c>Ui.DayAndTime</c> and <c>Ui.DatePicker.Typed()</c> on the UI tab (#1297), in a browser: the day and the time
///     typed key by key, the time waiting for a day, the seconds a moment keeps, and the row that puts the time beneath
///     the day on a phone.
/// </summary>
public sealed class UiKitDayAndTimeTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    private static readonly KeyboardTypeOptions Keys = new() { Delay = 60 };

    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task A_day_and_then_a_time_typed_are_saved_as_one_moment() => RunAsync(async () =>
    {
        await OpenUiPageAsync("day-and-time", "Day and time");
        var pair = Page.Locator("[data-testid='ui-day-and-time'] [data-ui-day-and-time]");
        var hour = pair.Locator("[data-ui-time-picker] [data-rask-segment='hour']");
        await pair.ScrollIntoViewIfNeededAsync();
        await Expect(hour).ToBeDisabledAsync();

        // The site reads English: month, day, year. 3 can only be March, so it moves on.
        await pair.Locator("[data-ui-date-picker] [data-rask-segment='month']").ClickAsync();
        await Page.Keyboard.TypeAsync("3152027", Keys);
        await Expect(hour).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 15_000 });
        await hour.ClickAsync();
        await Page.Keyboard.TypeAsync("0930a", Keys);
        await Page.Locator("[data-testid='ui-day-and-time'] button[type='submit']").ClickAsync();

        await Expect(Page.Locator("#udt-said")).ToHaveTextAsync("Saved 2027-03-15 09:30.", new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
    });

    [Fact]
    public Task On_a_phone_the_time_stands_beneath_the_day() => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(390, 844);
        await OpenUiPageAsync("day-and-time", "Day and time");
        var pair = Page.Locator("[data-testid='ui-day-and-time'] [data-ui-day-and-time]");
        await pair.ScrollIntoViewIfNeededAsync();

        var day = await pair.Locator("> [data-ui-date-picker]").BoundingBoxAsync();
        var time = await pair.Locator("> [data-ui-time-picker]").BoundingBoxAsync();
        var row = await pair.BoundingBoxAsync();

        Assert.NotNull(day);
        Assert.NotNull(time);
        Assert.True(time.Y >= day.Y + day.Height, $"the time at {time.Y} is beside the day ending at {day.Y + day.Height}");
        Assert.True(day.Width <= row!.Width + 0.5, "the day runs past its row");
        Assert.Equal(0, await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth"));
    });
}
