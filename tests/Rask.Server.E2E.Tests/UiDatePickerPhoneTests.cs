using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The kit's date picker at a phone's width, in a real browser over the live socket: under 640 px the popup is
///     Flux UI's sheet in the middle of the screen, a pick marks a day and writes nothing, and the button at its
///     foot writes it. On a wide window the same picker writes on the pick, as it always did.
/// </summary>
public sealed class UiDatePickerPhoneTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private static readonly BrowserNewContextOptions Phone = new() { ViewportSize = new() { Width = 390, Height = 844 } };

    private static ILocator Picker(IPage page, int nth) => page.Locator("[data-ui-date-picker]").Nth(nth);

    private static ILocator Day(ILocator picker, int day) =>
        picker.Locator("dialog td:not([data-outside]) button").Filter(new() { HasTextRegex = new Regex("^\\s*" + day + "\\s*$") });

    private static string ThisMonth(int day)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return new DateOnly(today.Year, today.Month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // The sheet is open and the page has heard it is one: no day closes the popup any more.
    private static async Task<ILocator> OpenSheetAsync(IPage page, int nth)
    {
        var picker = Picker(page, nth);
        await picker.Locator("button[role=combobox]").ClickAsync();
        await Expect(picker.Locator("dialog")).ToBeVisibleAsync();
        await Expect(picker.Locator("dialog [popovertargetaction]")).ToHaveCountAsync(0);
        return picker;
    }

    [Fact]
    public async Task On_a_phone_the_popup_is_a_sheet_in_the_middle_and_a_pick_writes_nothing_until_select_date()
    {
        await using var session = await HookSession.OpenAsync<DatePickerPhonePage>(playwright, Phone);
        await session.HooksLoadedAsync();
        var page = session.Page;

        var picker = await OpenSheetAsync(page, 0);
        var box = await picker.Locator("dialog").BoundingBoxAsync();
        await Day(picker, 15).ClickAsync();
        await Expect(picker.Locator("td[data-selected]")).ToHaveTextAsync("15");
        var whileMarked = await page.Locator("#day-state").TextContentAsync();
        var stillOpen = await picker.Locator("dialog").IsVisibleAsync();
        await picker.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Select date", Exact = true }).Last.ClickAsync();

        await Expect(page.Locator("#day-state")).ToHaveTextAsync(ThisMonth(15));
        await Expect(picker.Locator("dialog")).ToBeHiddenAsync();
        Assert.Equal("none", whileMarked);
        Assert.True(stillOpen);
        // In the middle both ways. (How wide it is belongs to the page's reset, which this page has none of.)
        Assert.Equal(Math.Round((390 - box!.Width) / 2), Math.Round(box.X));
        Assert.Equal(Math.Round((844 - box.Height) / 2), Math.Round(box.Y));
    }

    [Fact]
    public async Task Cancel_escape_and_a_press_on_the_dimmed_page_each_close_the_sheet_and_write_nothing()
    {
        await using var session = await HookSession.OpenAsync<DatePickerPhonePage>(playwright, Phone);
        await session.HooksLoadedAsync();
        var page = session.Page;

        var picker = await OpenSheetAsync(page, 0);
        await Day(picker, 15).ClickAsync();
        await picker.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Expect(picker.Locator("dialog")).ToBeHiddenAsync();
        await OpenSheetAsync(page, 0);
        await Day(picker, 16).ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(picker.Locator("dialog")).ToBeHiddenAsync();
        await OpenSheetAsync(page, 0);
        await Day(picker, 17).ClickAsync();
        await page.Mouse.ClickAsync(8, 30);

        await Expect(picker.Locator("dialog")).ToBeHiddenAsync();
        await Expect(page.Locator("#day-state")).ToHaveTextAsync("none");
    }

    [Fact]
    public async Task A_range_on_a_phone_draws_one_month_and_select_range_writes_both_days()
    {
        await using var session = await HookSession.OpenAsync<DatePickerPhonePage>(playwright, Phone);
        await session.HooksLoadedAsync();
        var page = session.Page;

        var picker = await OpenSheetAsync(page, 1);
        await Expect(picker.Locator("[data-month]")).ToHaveCountAsync(1);
        await Day(picker, 10).ClickAsync();
        await Day(picker, 12).ClickAsync();
        await Expect(picker.Locator("td[data-end]")).ToHaveTextAsync("12");
        var whileMarked = await page.Locator("#stay-state").TextContentAsync();
        await picker.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Select range", Exact = true }).Last.ClickAsync();

        await Expect(page.Locator("#stay-state")).ToHaveTextAsync(ThisMonth(10) + ".." + ThisMonth(12));
        Assert.Equal("none", whileMarked);
    }

    [Fact]
    public async Task On_a_wide_window_the_same_picker_hangs_from_its_trigger_and_writes_on_the_pick()
    {
        await using var session = await HookSession.OpenAsync<DatePickerPhonePage>(playwright);
        await session.HooksLoadedAsync();
        var page = session.Page;

        var picker = Picker(page, 0);
        await picker.Locator("button[role=combobox]").ClickAsync();
        await Expect(picker.Locator("dialog")).ToBeVisibleAsync();
        var trigger = await picker.Locator("button[role=combobox]").BoundingBoxAsync();
        var box = await picker.Locator("dialog").BoundingBoxAsync();
        var footer = await picker.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Cancel", Exact = true }).IsVisibleAsync();
        await Day(picker, 15).ClickAsync();

        await Expect(page.Locator("#day-state")).ToHaveTextAsync(ThisMonth(15));
        await Expect(picker.Locator("dialog")).ToBeHiddenAsync();
        Assert.Equal(Math.Round(trigger!.Y + trigger.Height + 5), Math.Round(box!.Y));
        Assert.Equal(Math.Round(trigger.X), Math.Round(box.X));
        Assert.False(footer);
    }

    [Fact]
    public async Task A_window_narrowed_with_the_popup_open_turns_it_into_the_sheet_and_the_next_pick_waits()
    {
        await using var session = await HookSession.OpenAsync<DatePickerPhonePage>(playwright);
        await session.HooksLoadedAsync();
        var page = session.Page;

        var picker = Picker(page, 0);
        await picker.Locator("button[role=combobox]").ClickAsync();
        await Expect(picker.Locator("dialog")).ToBeVisibleAsync();
        await page.SetViewportSizeAsync(390, 844);
        await Expect(picker.Locator("dialog [popovertargetaction]")).ToHaveCountAsync(0);
        var box = await picker.Locator("dialog").BoundingBoxAsync();
        await Day(picker, 15).ClickAsync();
        await Expect(picker.Locator("td[data-selected]")).ToHaveTextAsync("15");

        await Expect(picker.Locator("dialog")).ToBeVisibleAsync();
        await Expect(page.Locator("#day-state")).ToHaveTextAsync("none");
        Assert.Equal(Math.Round((390 - box!.Width) / 2), Math.Round(box.X));
    }

    [Fact]
    public async Task Moving_the_pointer_over_the_days_of_the_sheet_sends_the_server_nothing()
    {
        var sent = 0;
        await using var session = await HookSession.OpenAsync<DatePickerPhonePage>(playwright, Phone, beforeLoad: page =>
        {
            page.WebSocket += (_, socket) => socket.FrameSent += (_, _) => Interlocked.Increment(ref sent);
            return Task.CompletedTask;
        });
        await session.HooksLoadedAsync();
        var page = session.Page;
        var picker = await OpenSheetAsync(page, 0);
        await page.WaitForTimeoutAsync(300);
        var before = Volatile.Read(ref sent);

        foreach (var day in new[] { 8, 9, 16, 23 })
        {
            await Day(picker, day).HoverAsync();
        }

        await page.WaitForTimeoutAsync(300);

        Assert.Equal(before, Volatile.Read(ref sent));
    }
}

/// <summary>A day and a range, each with the value the page holds written beside it, under the kit's own stylesheet.</summary>
public sealed partial class DatePickerPhonePage : Component
{
    private DateOnly _day;
    private UiDateRange _stay;

    protected override Component? HeadAssets => [Markup.Title["date picker on a phone"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
        Div.Style("padding:24px")[
            P.Id("day-state")[_day == default ? "none" : Iso(_day)],
            Ui.DatePicker.Value(_day).OnChange(day => { _day = day; }).Locale("en-US"),
            P.Id("stay-state")[_stay == default ? "none" : Iso(_stay.Start) + ".." + Iso(_stay.End)],
            Ui.DatePicker.Range.Value(_stay).OnChange(stay => { _stay = stay; }).Locale("en-US")
        ];

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
