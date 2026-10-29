using Microsoft.Playwright;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A typed <c>Ui.DatePicker</c> bound in a form and NOT live: what is typed goes with the next action, and the
///     calendar it opens is drawn in the browser — so it opens on the typed date with nothing sent, and on today
///     for parts left half filled. A day picked in it is written into the parts the same way.
/// </summary>
public sealed class TypedDateOpensTheCalendarTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Popup = "[data-ui-date-picker]:has(#leaves) [popover]";

    private static ILocator Part(IPage page, string kind) => page.Locator($"#leaves [data-rask-segment=\"{kind}\"]");

    private async Task<(HookSession Session, SocketFrames Frames)> OpenAsync()
    {
        SocketFrames? frames = null;
        var session = await HookSession.OpenAsync<PickerEditPage>(playwright, beforeLoad: opened =>
        {
            frames = SocketFrames.Of(opened);
            return Task.CompletedTask;
        });
        await session.HooksLoadedAsync();
        // The socket's hello is behind what a journey counts (SocketHello).
        await frames!.OpenedAsync();
        return (session, frames);
    }

    private static async Task OpenCalendarAsync(IPage page)
    {
        await page.Locator("#leaves > svg").Last.ClickAsync();
        await Expect(page.Locator(Popup)).ToBeVisibleAsync();
    }

    private static Task<string> TodayAsync(IPage page) =>
        page.EvaluateAsync<string>("s => JSON.parse(document.querySelector(s).closest('[data-rask-calendar]').getAttribute('data-rask-calendar')).today", Popup);

    [Fact]
    public async Task A_whole_date_typed_and_not_yet_sent_is_the_day_the_calendar_opens_on_and_opening_sends_nothing()
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;

        await Part(page, "month").ClickAsync();
        await page.Keyboard.TypeAsync("08202026");
        var sentBefore = frames.Sent;
        await OpenCalendarAsync(page);
        await Expect(page.Locator(Popup + " td[data-selected]")).ToHaveAttributeAsync("data-date", "2026-08-20");
        var sentByOpening = frames.Sent - sentBefore;
        await page.Keyboard.PressAsync("Escape");
        await page.ClickAsync("#save");

        Assert.Equal(0, sentByOpening);
        await Expect(page.Locator("#saved")).ToContainTextAsync("saved=2026-08-20|");
    }

    [Fact]
    public async Task Parts_left_half_filled_open_the_calendar_on_today_with_nothing_marked()
    {
        var (session, _) = await OpenAsync();
        await using var __ = session;
        var page = session.Page;

        await Part(page, "day").ClickAsync();
        await page.Keyboard.PressAsync("Backspace");
        await OpenCalendarAsync(page);
        var today = await TodayAsync(page);

        await Expect(page.Locator(Popup + " td[data-selected]")).ToHaveCountAsync(0);
        await Expect(page.Locator(Popup + $" td[data-date=\"{today}\"]:not([data-outside])")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task A_day_picked_in_the_calendar_is_shown_in_the_parts_before_anything_is_sent()
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;
        await OpenCalendarAsync(page);
        var sentBefore = frames.Sent;

        await page.Locator(Popup + " td[data-date=\"2026-03-20\"] button").ClickAsync();

        await Expect(Part(page, "day")).ToHaveValueAsync("20");
        await Expect(Part(page, "month")).ToHaveValueAsync("03");
        Assert.Equal(0, frames.Sent - sentBefore);
    }
}
