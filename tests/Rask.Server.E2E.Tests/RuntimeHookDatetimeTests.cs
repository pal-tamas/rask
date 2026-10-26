using System.Globalization;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A datetime picker drawn in the browser (<c>data-rask-calendar</c> with its <c>time</c>), in a real browser, on a
///     page a session drives.
/// </summary>
/// <remarks>
///     Recorded on Flux UI's live datetime picker (2026-10-22): a day alone or a time alone is held; with both, a time
///     picked writes the value and closes the popup, and a day picked beside a chosen time writes it and leaves it open;
///     a range, typed times and a confirmation wait for Select; a range whose end is not after its start says so and
///     cannot be selected; the column's arrows walk the times. What the page hears is counted beside each value.
/// </remarks>
public sealed class RuntimeHookDatetimeTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private static string Day(string root, string date) => root + " td[data-date='" + date + "'] button";

    private static string Time(string root, string time) => root + " [data-rask-calendar-part=times] [data-time='" + time + "']";

    private static string Listed(string root, string end, string time) =>
        root + " [data-ui-datetime-picker-time=" + end + "] [data-rask-calendar-part=time-list] [data-time='" + time + "']";

    [Fact]
    public async Task A_day_alone_is_held_and_a_time_picked_beside_it_is_the_value_and_closes_the_popup()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#single");

        await page.ClickAsync(Day("#single-box", "2026-01-20"));
        await page.WaitForTimeoutAsync(300);
        var held = await page.TextContentAsync("#single-heard");
        await page.ClickAsync(Time("#single-box", "10:00"));

        Assert.Equal("0", held);
        await Expect(page.Locator("#single-value")).ToHaveTextAsync("2026-01-20T10:00");
        await Expect(page.Locator("#single-heard")).ToHaveTextAsync("1");
        await Expect(page.Locator("#single-box dialog")).Not.ToBeVisibleAsync();
        await Expect(page.Locator("#single")).ToHaveTextAsync("Jan 20, 2026 at 10:00 AM");
    }

    [Fact]
    public async Task With_a_time_chosen_a_day_picked_is_the_value_at_once_and_the_popup_stays()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#chosen");

        await page.ClickAsync(Day("#chosen-box", "2026-01-21"));

        await Expect(page.Locator("#chosen-value")).ToHaveTextAsync("2026-01-21T09:30");
        await Expect(page.Locator("#chosen-heard")).ToHaveTextAsync("1");
        await Expect(page.Locator("#chosen-box dialog")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_columns_arrows_walk_the_times_and_Enter_picks_the_one_in_focus()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#chosen");
        await page.FocusAsync(Time("#chosen-box", "09:30"));

        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("ArrowUp");
        var walked = await page.EvaluateAsync<string>("() => document.activeElement.dataset.time");
        await page.Keyboard.PressAsync("Enter");

        Assert.Equal("10:00", walked);
        await Expect(page.Locator("#chosen-value")).ToHaveTextAsync("2026-01-15T10:00");
        await Expect(page.Locator("#chosen-box dialog")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_range_waits_for_Select_range_and_cannot_be_selected_while_its_end_is_not_after_its_start()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#range");

        await page.ClickAsync(Day("#range-box", "2026-01-20"));
        await page.ClickAsync(Day("#range-box", "2026-01-20"));
        await page.ClickAsync("#range-box [data-ui-datetime-picker-time=start] button[role=combobox]");
        await page.ClickAsync(Listed("#range-box", "start", "10:00"));
        await page.ClickAsync("#range-box [data-ui-datetime-picker-time=end] button[role=combobox]");
        await page.ClickAsync(Listed("#range-box", "end", "09:00"));
        var crossed = (await page.IsVisibleAsync("#range-box [data-ui-datetime-picker-error]"),
            await page.IsDisabledAsync("#range-box [data-ui-datetime-picker-select]"));
        await page.ClickAsync("#range-box [data-ui-datetime-picker-time=end] button[role=combobox]");
        await page.ClickAsync(Listed("#range-box", "end", "17:00"));
        await page.WaitForTimeoutAsync(300);
        var heard = await page.TextContentAsync("#range-heard");
        await page.ClickAsync("#range-box [data-ui-datetime-picker-select]");

        Assert.Equal((true, true), crossed);
        Assert.Equal("0", heard);
        await Expect(page.Locator("#range-value")).ToHaveTextAsync("2026-01-20T10:00/2026-01-20T17:00");
        await Expect(page.Locator("#range")).ToHaveTextAsync("Jan 20, 10:00 AM – 5:00 PM");
    }

    [Fact]
    public async Task On_the_mins_day_the_earlier_times_cannot_be_picked_and_the_window_lists_no_other()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#limited");

        await page.ClickAsync(Day("#limited-box", "2026-01-15"));
        var listed = await page.Locator("#limited-box [data-rask-calendar-part=times] [data-time]").CountAsync();
        var off = (await page.IsDisabledAsync(Time("#limited-box", "12:30")), await page.IsDisabledAsync(Time("#limited-box", "13:00")));
        await page.ClickAsync(Day("#limited-box", "2026-01-16"));

        Assert.Equal(17, listed);
        Assert.Equal((true, false), off);
        Assert.False(await page.IsDisabledAsync(Time("#limited-box", "09:00")));
    }

    [Fact]
    public async Task A_typed_time_waits_for_Select()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#typed");

        await page.ClickAsync(Day("#typed-box", "2026-01-20"));
        await page.ClickAsync("#typed-box [data-rask-segment=hour]");
        await page.Keyboard.TypeAsync("0945a");
        await page.WaitForTimeoutAsync(300);
        var heard = await page.TextContentAsync("#typed-heard");
        await page.ClickAsync("#typed-box [data-ui-datetime-picker-select]");

        Assert.Equal("0", heard);
        await Expect(page.Locator("#typed-value")).ToHaveTextAsync("2026-01-20T09:45");
        await Expect(page.Locator("#typed-heard")).ToHaveTextAsync("1");
    }

    [Fact]
    public async Task With_confirmation_a_whole_choice_waits_and_Escape_drops_it()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#confirmed");

        await page.ClickAsync(Day("#confirmed-box", "2026-01-22"));
        await page.ClickAsync(Time("#confirmed-box", "11:00"));
        var open = await page.IsVisibleAsync("#confirmed-box dialog");
        await page.Keyboard.PressAsync("Escape");
        await page.ClickAsync("#confirmed");
        var marked = await page.Locator("#confirmed-box td[data-selected]").CountAsync();

        Assert.True(open);
        Assert.Equal(0, marked);
        Assert.Equal("0", await page.TextContentAsync("#confirmed-heard"));
    }

    [Fact]
    public async Task The_cross_in_the_button_empties_the_picker_without_opening_it()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.ClickAsync("#chosen [aria-label='Clear date and time']");

        Assert.False(await page.IsVisibleAsync("#chosen-box dialog"));
        await Expect(page.Locator("#chosen")).ToContainTextAsync("Select a date and time");
        await Expect(page.Locator("#chosen [aria-label='Clear date and time']")).ToBeHiddenAsync();
        Assert.Equal("BODY", await page.EvaluateAsync<string>("document.activeElement.tagName"));
        await Expect(page.Locator("#chosen-value")).ToHaveTextAsync(string.Empty);
    }

    [Fact]
    public async Task On_a_phone_the_time_is_a_field_under_the_months_and_Select_writes_the_choice()
    {
        await using var session = await OpenAsync(new() { ViewportSize = new() { Width = 390, Height = 844 }, HasTouch = true });
        var page = session.Page;
        await page.ClickAsync("#single");

        await page.ClickAsync(Day("#single-box", "2026-01-20"));
        await page.ClickAsync("#single-box [data-ui-datetime-picker-time=single] button[role=combobox]");
        await page.ClickAsync(Listed("#single-box", "single", "12:00"));
        var held = (await page.TextContentAsync("#single-heard"), await page.IsVisibleAsync("#single-box [data-rask-calendar-part=times]"));
        await page.ClickAsync("#single-box [data-ui-datetime-picker-select]");

        Assert.Equal(("0", false), held);
        await Expect(page.Locator("#single-value")).ToHaveTextAsync("2026-01-20T12:00");
    }

    private async Task<HookSession> OpenAsync(BrowserNewContextOptions? options = null)
    {
        var session = await HookSession.OpenAsync<DatetimeHookPage>(playwright, options);
        await session.HooksLoadedAsync();
        return session;
    }
}

/// <summary>Datetime pickers whose page hears every value they take, and counts them.</summary>
public sealed partial class DatetimeHookPage : Component
{
    private static readonly DateOnly Jan15 = new(2026, 1, 15);

    private DateTime _single;
    private DateTime? _chosen = new DateTime(2026, 1, 15, 9, 30, 0);
    private UiDateTimeRange _range;
    private DateTime _limited;
    private DateTime _typed;
    private DateTime _confirmed;
    private readonly Dictionary<string, int> _heard = new(StringComparer.Ordinal);

    protected override Component? HeadAssets => [Markup.Title["datetime hook"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
        Div.Style("padding:16px;padding-bottom:600px")[
            Box("single", Ui.DatetimePicker.Value(_single).Id("single").Locale("en-US").OpenTo(Jan15.ToDateTime(TimeOnly.MinValue)).OnChange(d => Heard("single", () => _single = d))),
            Said("single", Moment(_single)),
            Box("chosen", Ui.DatetimePicker.Value(_chosen ?? default).Id("chosen").Locale("en-US").Clearable()
                .OnChange(d => Heard("chosen", () => _chosen = d == default ? null : d))),
            Said("chosen", _chosen is { } c ? Moment(c) : string.Empty),
            Box("range", Ui.DatetimePicker.Range.Value(_range).Id("range").Locale("en-US").Months(1).OpenTo(Jan15.ToDateTime(TimeOnly.MinValue))
                .OnChange(r => Heard("range", () => _range = r))),
            Said("range", _range == default ? string.Empty : Moment(_range.Start) + "/" + Moment(_range.End)),
            Box("limited", Ui.DatetimePicker.Value(_limited).Id("limited").Locale("en-US")
                .Min(new DateTime(2026, 1, 15, 13, 0, 0)).MinTime(new TimeOnly(9, 0)).MaxTime(new TimeOnly(17, 0)).OpenTo(Jan15.ToDateTime(TimeOnly.MinValue))
                .OnChange(d => Heard("limited", () => _limited = d))),
            Box("typed", Ui.DatetimePicker.Value(_typed).Id("typed").Locale("en-US").Time(Ui.DatetimePickerTime.Input).OpenTo(Jan15.ToDateTime(TimeOnly.MinValue))
                .OnChange(d => Heard("typed", () => _typed = d))),
            Said("typed", Moment(_typed)),
            Box("confirmed", Ui.DatetimePicker.Value(_confirmed).Id("confirmed").Locale("en-US").WithConfirmation().OpenTo(Jan15.ToDateTime(TimeOnly.MinValue))
                .OnChange(d => Heard("confirmed", () => _confirmed = d))),
            Said("confirmed", Moment(_confirmed))
        ];

    private static Component Box(string name, Component picker) => Div.Id(name + "-box").Style("width:320px;margin-bottom:8px")[picker];

    private static string Moment(DateTime moment) =>
        moment == default ? string.Empty : moment.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

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
