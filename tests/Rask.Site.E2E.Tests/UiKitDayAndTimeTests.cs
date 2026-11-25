using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     <c>Ui.DayAndTime</c> and <c>Ui.DatePicker</c> typed (its default) on the UI tab (#1297), in a browser: the day and the time
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

    [Fact]
    public Task A_day_and_a_time_kept_apart_say_two_messages_then_save_each_member() => RunAsync(async () =>
    {
        await OpenUiPageAsync("day-and-time", "Day and time");
        var example = Page.Locator("[data-testid='ui-day-and-time-parts']");
        var hour = example.Locator("[data-ui-time-picker] [data-rask-segment='hour']");
        await example.ScrollIntoViewIfNeededAsync();
        await Expect(hour).ToBeDisabledAsync();

        await example.Locator("button[type='submit']").ClickAsync();
        await Expect(Page.Locator("#udt-parts-error")).ToHaveTextAsync("The Day field is required.", new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
        await Expect(Page.Locator("#udt-parts-time-error")).ToHaveTextAsync("The Time field is required.");
        await example.Locator("[data-ui-date-picker] [data-rask-segment='month']").ClickAsync();
        await Page.Keyboard.TypeAsync("3152027", Keys);
        await Expect(hour).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 15_000 });
        await hour.ClickAsync();
        await Page.Keyboard.TypeAsync("0930a", Keys);
        await example.Locator("button[type='submit']").ClickAsync();

        await Expect(Page.Locator("#udt-parts-said")).ToHaveTextAsync("Saved 2027-03-15 at 09:30.", new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });
    });

    [Fact]
    public Task On_a_phone_a_rule_of_the_day_speaks_under_the_day_and_the_time_beneath_it() => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(390, 844);
        await OpenUiPageAsync("day-and-time", "Day and time");
        var example = Page.Locator("[data-testid='ui-day-and-time-parts']");
        var hour = example.Locator("[data-ui-time-picker] [data-rask-segment='hour']");
        await example.ScrollIntoViewIfNeededAsync();

        // 14 March 2027 is a Sunday, which the day's own rule refuses; the time is fine.
        await example.Locator("[data-ui-date-picker] [data-rask-segment='month']").ClickAsync();
        await Page.Keyboard.TypeAsync("3142027", Keys);
        await Expect(hour).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 15_000 });
        await hour.ClickAsync();
        await Page.Keyboard.TypeAsync("0930a", Keys);
        await example.Locator("button[type='submit']").ClickAsync();
        await Expect(Page.Locator("#udt-parts-error")).ToHaveTextAsync("Nothing is delivered on a Sunday.", new LocatorAssertionsToHaveTextOptions { Timeout = 15_000 });

        var day = await example.Locator("[data-ui-date-picker]").BoundingBoxAsync();
        var said = await Page.Locator("#udt-parts-error").BoundingBoxAsync();
        var time = await example.Locator("[data-ui-time-picker]").BoundingBoxAsync();
        Assert.NotNull(day);
        Assert.NotNull(said);
        Assert.NotNull(time);
        Assert.True(said.Y >= day.Y + day.Height, $"the day's message at {said.Y} is not under the day ending at {day.Y + day.Height}");
        Assert.True(time.Y >= said.Y + said.Height, $"the time at {time.Y} is beside the day's message ending at {said.Y + said.Height}");
        await Expect(Page.Locator("#udt-parts-time-error")).ToBeHiddenAsync();
        await Expect(Page.Locator("#udt-parts-said")).ToHaveTextAsync("Nothing saved yet.");
        Assert.Equal(0, await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth"));
    });
}
