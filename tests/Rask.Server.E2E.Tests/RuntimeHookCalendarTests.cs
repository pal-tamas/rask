using System.Globalization;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A calendar and a date picker drawn in the browser (<c>data-rask-calendar</c>), in a real browser, on a page a
///     session drives.
/// </summary>
/// <remarks>
///     Recorded on Flux UI's live calendar and date picker (2026-10-15): a step, a select and the paging keys page
///     the month and announce nothing, the arrows walk the days, a day picked announces the value, a range's first
///     day announces nothing and its last the range, and a date picker's popup opens and closes with nothing
///     announced. What the page hears is counted beside each value (<c>#…-heard</c>).
/// </remarks>
public sealed class RuntimeHookCalendarTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private static string Day(string root, string date) => root + " td[data-date='" + date + "'] button";

    private static Task<string> FocusedDayAsync(IPage page) =>
        page.EvaluateAsync<string>("() => document.activeElement.closest('td') ? document.activeElement.closest('td').dataset.date : document.activeElement.tagName");

    private static Task<string> HeadingAsync(IPage page, string root) =>
        page.EvaluateAsync<string>("s => document.querySelector(s + ' [role=grid] div').textContent.trim()", root);

    [Fact]
    public async Task The_arrows_walk_days_and_weeks_over_a_day_that_cannot_be_picked_and_the_page_hears_nothing()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.FocusAsync(Day("#single", "2026-01-15"));

        var walked = new List<string>();
        foreach (var key in new[] { "ArrowRight", "ArrowDown", "ArrowLeft", "ArrowUp" })
        {
            await page.Keyboard.PressAsync(key);
            walked.Add(await FocusedDayAsync(page));
        }

        Assert.Equal(["2026-01-17", "2026-01-24", "2026-01-23", "2026-01-09"], walked);
        Assert.Equal("0", await page.GetAttributeAsync(Day("#single", "2026-01-09"), "tabindex"));
        await page.WaitForTimeoutAsync(300);
        Assert.Equal("0", await page.TextContentAsync("#single-heard"));
    }

    [Fact]
    public async Task An_arrow_past_the_drawn_weeks_takes_the_month_with_it_and_the_focus_follows()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.FocusAsync(Day("#single", "2026-01-15"));

        foreach (var key in new[] { "ArrowDown", "ArrowDown", "ArrowDown" })
        {
            await page.Keyboard.PressAsync(key);
        }

        Assert.Equal("2026-02-05", await FocusedDayAsync(page));
        Assert.Equal("February 2026", await HeadingAsync(page, "#single"));
    }

    [Fact]
    public async Task A_paging_key_pages_a_month_and_leaves_the_focus_to_the_page()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.FocusAsync(Day("#single", "2026-01-15"));

        await page.Keyboard.PressAsync("PageDown");
        var down = (await HeadingAsync(page, "#single"), await FocusedDayAsync(page));
        await page.Keyboard.PressAsync("Home");
        var body = await HeadingAsync(page, "#single");
        await page.FocusAsync("#single [role=gridcell] button[tabindex='0']");
        await page.Keyboard.PressAsync("Home");

        Assert.Equal(("February 2026", "BODY"), down);
        Assert.Equal("February 2026", body);
        Assert.Equal("January 2026", await HeadingAsync(page, "#single"));
        await page.WaitForTimeoutAsync(300);
        Assert.Equal("0", await page.TextContentAsync("#single-heard"));
    }

    [Fact]
    public async Task The_steps_page_the_month_and_the_next_one_is_off_once_the_whole_month_after_lies_past_max()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.ClickAsync("#single [aria-label='Next month']");
        var february = (await HeadingAsync(page, "#single"), await page.IsDisabledAsync("#single [aria-label='Next month']"));
        await page.ClickAsync("#single [aria-label='Previous month']");

        Assert.Equal(("February 2026", true), february);
        Assert.Equal("January 2026", await HeadingAsync(page, "#single"));
        Assert.False(await page.IsDisabledAsync("#single [aria-label='Next month']"));
        Assert.True(await page.IsDisabledAsync(Day("#single", "2026-01-16")));
        await page.WaitForTimeoutAsync(300);
        Assert.Equal("0", await page.TextContentAsync("#single-heard"));
    }

    [Fact]
    public async Task A_day_picked_is_the_pages_and_the_same_day_picked_again_clears_it()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.ClickAsync(Day("#single", "2026-01-20"));
        await Expect(page.Locator("#single-value")).ToHaveTextAsync("2026-01-20");
        var marked = await page.GetAttributeAsync("#single td[data-date='2026-01-20']", "aria-selected");
        await page.ClickAsync(Day("#single", "2026-01-20"));

        Assert.Equal("true", marked);
        await Expect(page.Locator("#single-value")).ToHaveTextAsync("0001-01-01");
        await Expect(page.Locator("#single-heard")).ToHaveTextAsync("2");
        Assert.Equal("false", await page.GetAttributeAsync("#single td[data-date='2026-01-20']", "aria-selected"));
    }

    [Fact]
    public async Task A_ranges_first_day_waits_unheard_the_pointer_draws_the_stretch_and_the_last_day_writes_it()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.ClickAsync(Day("#range", "2026-01-10"));
        await page.HoverAsync(Day("#range", "2026-01-13"));
        var drawn = (await page.GetAttributeAsync("#range td[data-date='2026-01-13']", "data-end-preview"),
            await page.GetAttributeAsync("#range td[data-date='2026-01-12']", "data-in-range"));
        var ruledOut = (await page.IsDisabledAsync(Day("#range", "2026-01-11")), await page.IsDisabledAsync(Day("#range", "2026-01-15")));
        await page.WaitForTimeoutAsync(300);
        var heard = await page.TextContentAsync("#range-heard");
        await page.ClickAsync(Day("#range", "2026-01-14"));

        Assert.Equal((string.Empty, string.Empty), drawn);
        Assert.Equal((true, true), ruledOut);
        Assert.Equal("0", heard);
        await Expect(page.Locator("#range-value")).ToHaveTextAsync("2026-01-10/2026-01-14");
        await Expect(page.Locator("#range-heard")).ToHaveTextAsync("1");
        Assert.Equal(string.Empty, await page.GetAttributeAsync("#range td[data-date='2026-01-14']", "data-end"));
    }

    [Fact]
    public async Task A_click_before_a_waiting_start_begins_the_range_again_from_there()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.ClickAsync("#stay");

        await page.ClickAsync(Day("#stay-box", "2026-01-20"));
        await page.ClickAsync(Day("#stay-box", "2026-01-12"));
        await page.ClickAsync(Day("#stay-box", "2026-01-15"));

        await Expect(page.Locator("#stay-value")).ToHaveTextAsync("2026-01-12/2026-01-15/");
        await Expect(page.Locator("#stay-heard")).ToHaveTextAsync("1");
    }

    [Fact]
    public async Task Of_several_days_a_click_adds_one_and_a_second_takes_it_out()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.ClickAsync(Day("#days", "2026-01-12"));
        await Expect(page.Locator("#days-value")).ToHaveTextAsync("2026-01-05,2026-01-12");
        await page.ClickAsync(Day("#days", "2026-01-05"));

        await Expect(page.Locator("#days-value")).ToHaveTextAsync("2026-01-12");
        await Expect(page.Locator("#days-heard")).ToHaveTextAsync("2");
    }

    [Fact]
    public async Task The_today_shortcut_comes_back_to_this_month_and_there_picks_today()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var today = DateOnly.FromDateTime(DateTime.Now);

        await page.ClickAsync("#today [aria-label='Today']");
        var heading = await HeadingAsync(page, "#today");
        await page.WaitForTimeoutAsync(300);
        var heard = await page.TextContentAsync("#today-heard");
        await page.ClickAsync("#today [aria-label='Today']");

        Assert.Equal(today.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-US")), heading);
        Assert.Equal("0", heard);
        await Expect(page.Locator("#today-value")).ToHaveTextAsync(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task The_headers_selects_page_to_the_month_and_the_year_they_name()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.SelectOptionAsync("#header [data-rask-calendar-part=month]", "3");
        await page.SelectOptionAsync("#header [data-rask-calendar-part=year]", "2027");

        Assert.Equal("March 2027", await page.EvaluateAsync<string>("() => document.querySelector('#header [role=grid] div').textContent.trim()"));
        Assert.Equal("2027-03-01", await page.GetAttributeAsync("#header td[data-date]:not([data-outside])", "data-date"));
    }

    [Fact]
    public async Task A_date_picker_opens_with_nothing_heard_and_a_pick_closes_it_and_writes_the_day_on_its_button()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.ClickAsync("#arrival");
        await Expect(page.Locator("#arrival")).ToHaveAttributeAsync("aria-expanded", "true");
        var open = await page.GetAttributeAsync("#arrival-box [data-ui-date-picker]", "data-open");
        await page.WaitForTimeoutAsync(300);
        var heard = await page.TextContentAsync("#arrival-heard");
        await page.ClickAsync(Day("#arrival-box", "2026-01-20"));

        Assert.Equal(string.Empty, open);
        Assert.Equal("0", heard);
        await Expect(page.Locator("#arrival-box [popover]")).ToBeHiddenAsync();
        await Expect(page.Locator("#arrival")).ToContainTextAsync("Jan 20, 2026");
        await Expect(page.Locator("#arrival-value")).ToHaveTextAsync("2026-01-20");
        await Expect(page.Locator("#arrival-heard")).ToHaveTextAsync("1");
    }

    [Fact]
    public async Task An_arrow_on_the_closed_button_opens_the_popup()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.FocusAsync("#arrival");

        await page.Keyboard.PressAsync("ArrowDown");

        await Expect(page.Locator("#arrival-box [popover]")).ToBeVisibleAsync();
        await Expect(page.Locator("#arrival")).ToHaveAttributeAsync("aria-expanded", "true");
    }

    [Fact]
    public async Task A_range_picker_stays_open_on_its_first_day_and_writes_both_on_its_button()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#stay");

        await page.ClickAsync(Day("#stay-box", "2026-01-10"));
        var stillOpen = await page.IsVisibleAsync("#stay-box [popover]");
        await page.ClickAsync(Day("#stay-box", "2026-01-14"));

        Assert.True(stillOpen);
        await Expect(page.Locator("#stay-box [popover]")).ToBeHiddenAsync();
        await Expect(page.Locator("#stay")).ToContainTextAsync("Jan 10, 2026 – Jan 14, 2026");
        await Expect(page.Locator("#stay-heard")).ToHaveTextAsync("1");
    }

    [Fact]
    public async Task A_preset_writes_its_range_puts_its_name_on_the_button_and_is_checked_when_the_popup_opens_again()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#stay");

        await page.ClickAsync("#stay-box [role=radio][value=last7Days]");
        await Expect(page.Locator("#stay")).ToContainTextAsync("Last 7 Days");
        await page.ClickAsync("#stay");

        await Expect(page.Locator("#stay-value")).ToContainTextAsync("/last7Days");
        Assert.Equal("true", await page.GetAttributeAsync("#stay-box [role=radio][value=last7Days]", "aria-checked"));
        Assert.Equal("0", await page.GetAttributeAsync("#stay-box [role=radio][value=last7Days]", "tabindex"));
    }

    [Fact]
    public async Task With_confirmation_a_pick_waits_for_the_button_and_Cancel_drops_it()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#confirmed");

        await page.ClickAsync(Day("#confirmed-box", "2026-01-20"));
        await page.WaitForTimeoutAsync(300);
        var held = (await page.TextContentAsync("#confirmed-heard"), await page.IsVisibleAsync("#confirmed-box [popover]"));
        await page.ClickAsync("#confirmed-box [data-rask-calendar-part=confirm] button");
        await Expect(page.Locator("#confirmed-value")).ToHaveTextAsync("2026-01-20");
        await page.ClickAsync("#confirmed");
        await page.ClickAsync(Day("#confirmed-box", "2026-01-22"));
        await page.ClickAsync("#confirmed-box [popover] [data-ui-button]:not([data-rask-calendar-part])");

        Assert.Equal(("0", true), held);
        await Expect(page.Locator("#confirmed-box [popover]")).ToBeHiddenAsync();
        await page.ClickAsync("#confirmed");
        await Expect(page.Locator("#confirmed-box td[data-date='2026-01-20']")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(page.Locator("#confirmed-box td[data-date='2026-01-22']")).ToHaveAttributeAsync("aria-selected", "false");
        Assert.Equal("1", await page.TextContentAsync("#confirmed-heard"));
    }

    [Fact]
    public async Task On_a_phone_a_pick_is_held_for_the_button_and_a_sheet_closed_without_it_keeps_its_mark()
    {
        await using var session = await OpenAsync(new() { ViewportSize = new() { Width = 390, Height = 844 }, HasTouch = true });
        var page = session.Page;
        await page.ClickAsync("#arrival");

        await page.ClickAsync(Day("#arrival-box", "2026-01-20"));
        await page.WaitForTimeoutAsync(300);
        var held = (await page.TextContentAsync("#arrival-heard"), await page.IsVisibleAsync("#arrival-box [popover]"));
        await page.Keyboard.PressAsync("Escape");
        await page.ClickAsync("#arrival");
        var kept = await page.GetAttributeAsync("#arrival-box td[data-date='2026-01-20']", "aria-selected");
        await page.ClickAsync("#arrival-box [data-rask-calendar-part=confirm] button");

        Assert.Equal(("0", true), held);
        Assert.Equal("true", kept);
        await Expect(page.Locator("#arrival-value")).ToHaveTextAsync("2026-01-20");
        await Expect(page.Locator("#arrival-heard")).ToHaveTextAsync("1");
    }

    [Fact]
    public async Task The_cross_in_the_button_empties_the_picker_without_opening_it_and_Tab_passes_it_by()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.FocusAsync("#arrival");
        await page.Keyboard.PressAsync("Tab");
        var tabbed = await page.EvaluateAsync<string?>("document.activeElement?.getAttribute('aria-label')");

        await page.ClickAsync("#arrival [aria-label='Clear']");

        Assert.NotEqual("Clear", tabbed);
        Assert.False(await page.IsVisibleAsync("#arrival-box [popover]"));
        Assert.Equal("BODY", await page.EvaluateAsync<string>("document.activeElement.tagName"));
        await Expect(page.Locator("#arrival")).ToContainTextAsync("Select a date");
        await Expect(page.Locator("#arrival-value")).ToHaveTextAsync("0001-01-01");
        await page.ClickAsync("#arrival");
        Assert.Equal(0, await page.Locator("#arrival-box td[aria-selected='true']").CountAsync());
    }

    private async Task<HookSession> OpenAsync(BrowserNewContextOptions? options = null)
    {
        var session = await HookSession.OpenAsync<CalendarHookPage>(playwright, options);
        await session.HooksLoadedAsync();
        return session;
    }
}

/// <summary>Calendars and date pickers whose page hears every value they take, and counts them.</summary>
public sealed partial class CalendarHookPage : Component
{
    private static readonly DateOnly Jan15 = new(2026, 1, 15);

    private DateOnly _single = Jan15;
    private UiDateRange _range;
    private List<DateOnly> _days = [new(2026, 1, 5)];
    private DateOnly _arrival = Jan15;
    private UiDateRange _stay;
    private DateOnly _confirmed = Jan15;
    private DateOnly _today = Jan15;
    private readonly Dictionary<string, int> _heard = new(StringComparer.Ordinal);

    protected override Component? HeadAssets => [Markup.Title["calendar hook"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
        Div.Style("padding:16px")[
            Ui.Calendar.Value(_single).Id("single").Locale("en-US").Unavailable([new(2026, 1, 16)]).Max(new DateOnly(2026, 2, 20)).WithToday()
                .OnChange(d => Heard("single", () => _single = d)),
            Said("single", _single.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Ui.Calendar.Range.Value(_range).Id("range").Locale("en-US").Months(1).OpenTo(Jan15).MinRange(3).MaxRange(5)
                .OnChange(r => Heard("range", () => _range = r)),
            Said("range", _range == default ? string.Empty : _range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "/" + _range.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Ui.Calendar.Multiple.Values(_days).Id("days").Locale("en-US")
                .OnChange(d => Heard("days", () => _days = [.. d])),
            Said("days", string.Join(',', _days.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))),
            Ui.Calendar.Value(Jan15).Id("header").Locale("en-US").SelectableHeader(),
            Ui.Calendar.Value(_today).Id("today").Locale("en-US").WithToday().OnChange(d => Heard("today", () => _today = d)),
            Said("today", _today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Div.Id("arrival-box").Style("width:320px")[
                Ui.DatePicker.Value(_arrival).Id("arrival").Locale("en-US").Clearable().OnChange(d => Heard("arrival", () => _arrival = d))
            ],
            Said("arrival", _arrival.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Div.Id("stay-box").Style("width:320px")[
                Ui.DatePicker.Range.Value(_stay).Id("stay").Locale("en-US").OpenTo(Jan15).WithPresets().OnChange(r => Heard("stay", () => _stay = r))
            ],
            Said("stay", _stay == default ? string.Empty : _stay.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "/" + _stay.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "/" + (_stay.Preset?.ToString() is { } p ? char.ToLowerInvariant(p[0]) + p[1..] : string.Empty)),
            Div.Id("confirmed-box").Style("width:320px")[
                Ui.DatePicker.Value(_confirmed).Id("confirmed").Locale("en-US").WithConfirmation().OnChange(d => Heard("confirmed", () => _confirmed = d))
            ],
            Said("confirmed", _confirmed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        ];

    private void Heard(string name, Action take)
    {
        take();
        _heard[name] = _heard.GetValueOrDefault(name) + 1;
    }

    private Component Said(string name, string value) =>
        Div[
            Span.Id(name + "-value")[value],
            " ",
            Span.Id(name + "-heard")[_heard.GetValueOrDefault(name).ToString(CultureInfo.InvariantCulture)]
        ];
}
