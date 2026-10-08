using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A date and a time typed a part at a time (<c>data-rask-segments</c>), in a real browser.
/// </summary>
/// <remarks>
///     Written to what Flux UI's typed date picker and time picker did on their live pages on 2026-10-07: a
///     digit no second digit could follow finishes its part and moves on (3 is March, 9 is nine o'clock); two
///     digits finish it; the horizontal arrows walk the parts and stop at the ends; the vertical arrows step a
///     part and wrap; Backspace empties a part and, on an empty one, steps back; a pasted date is shared out;
///     a day the month does not have becomes its last. The value travels as one hidden field, as a one-time
///     code's does.
/// </remarks>
public sealed class RuntimeHookSegmentsTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Date = "() => ['month', 'day', 'year'].map(k => document.querySelector('#date [data-rask-segment=' + k + ']').value).join('/') + ' = ' + document.getElementById('date-value').value";
    private const string Time = "id => ['hour', 'minute', 'meridiem'].map(k => document.querySelector('#' + id + ' [data-rask-segment=' + k + ']').value).join(':') + ' = ' + document.querySelector('#' + id + ' input[type=hidden]').value";
    private const string Part = "() => document.activeElement.getAttribute('data-rask-segment')";

    private static async Task Type(Microsoft.Playwright.IPage page, string keys)
    {
        foreach (var key in keys)
        {
            await page.Keyboard.PressAsync(key.ToString());
        }
    }

    [Fact]
    public async Task Digits_fill_a_date_part_by_part_and_the_whole_date_is_announced_once_through_the_hidden_field()
    {
        await using var session = await HookSession.OpenAsync<SegmentsHookPage>(playwright);
        var page = session.Page;
        await page.EvaluateAsync("() => { window.heard = []; document.addEventListener('change', e => window.heard.push(e.target.id + '=' + e.target.value)); }");

        await page.FocusAsync("#date [data-rask-segment=month]");
        await Type(page, "3");
        var afterMonth = await page.EvaluateAsync<string>(Part);
        await Type(page, "14");
        var afterDay = await page.EvaluateAsync<string>(Part);
        await Type(page, "202");
        var unfinished = await page.InputValueAsync("#date-value");
        await Type(page, "6");

        // 3 can only be March, so it is finished at once; 1 could be 10 to 19, so the day waits for its second digit.
        Assert.Equal("day", afterMonth);
        Assert.Equal("year", afterDay);
        Assert.Equal(string.Empty, unfinished);
        Assert.Equal("03/14/2026 = 2026-03-14", await page.EvaluateAsync<string>(Date));
        Assert.Equal("date-value=2026-03-14", await page.EvaluateAsync<string>("() => window.heard.join(' ')"));
    }

    [Fact]
    public async Task The_horizontal_arrows_walk_the_parts_and_stop_at_the_ends_and_the_vertical_ones_step_and_wrap()
    {
        await using var session = await HookSession.OpenAsync<SegmentsHookPage>(playwright);
        var page = session.Page;

        await page.FocusAsync("#date [data-rask-segment=month]");
        await page.Keyboard.PressAsync("ArrowLeft");
        var atTheStart = await page.EvaluateAsync<string>(Part);
        await page.Keyboard.PressAsync("ArrowUp");
        await page.Keyboard.PressAsync("ArrowDown");
        var month = await page.InputValueAsync("#date [data-rask-segment=month]");
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("ArrowRight");

        // Up on an empty month is 01, and down from 01 wraps to 12.
        Assert.Equal("month", atTheStart);
        Assert.Equal("12", month);
        Assert.Equal("year", await page.EvaluateAsync<string>(Part));
        Assert.Equal(0, await page.EvaluateAsync<double>("() => scrollY"));
    }

    [Fact]
    public async Task Backspace_empties_a_part_and_on_an_empty_one_steps_back_without_taking_the_value_away()
    {
        await using var session = await HookSession.OpenAsync<SegmentsHookPage>(playwright);
        var page = session.Page;

        await page.FocusAsync("#date [data-rask-segment=month]");
        await Type(page, "12152026");
        await page.Keyboard.PressAsync("Backspace");
        var emptied = await page.EvaluateAsync<string>(Part);
        await page.Keyboard.PressAsync("Backspace");
        var steppedBack = await page.EvaluateAsync<string>(Part);
        await page.FocusAsync("#elsewhere");

        Assert.Equal("year", emptied);
        Assert.Equal("day", steppedBack);
        Assert.Equal("12/15/ = 2026-12-15", await page.EvaluateAsync<string>(Date));
    }

    [Fact]
    public async Task A_day_the_month_does_not_have_becomes_its_last_and_a_pasted_date_is_shared_out_among_the_parts()
    {
        await using var session = await HookSession.OpenAsync<SegmentsHookPage>(playwright);
        var page = session.Page;

        await page.FocusAsync("#date [data-rask-segment=month]");
        await Type(page, "02312025");
        var clamped = await page.EvaluateAsync<string>(Date);
        await page.EvaluateAsync("() => { const dt = new DataTransfer(); dt.setData('text', '2024-02-29'); document.activeElement.dispatchEvent(new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true })); }");

        Assert.Equal("02/28/2025 = 2025-02-28", clamped);
        Assert.Equal("02/29/2024 = 2024-02-29", await page.EvaluateAsync<string>(Date));
    }

    [Fact]
    public async Task A_time_is_typed_on_a_twelve_hour_clock_and_carried_on_a_twenty_four_hour_one()
    {
        await using var session = await HookSession.OpenAsync<SegmentsHookPage>(playwright);
        var page = session.Page;

        await page.FocusAsync("#time [data-rask-segment=hour]");
        await Type(page, "930");
        var morning = await page.EvaluateAsync<string>(Time, "time");
        var after = await page.EvaluateAsync<string>(Part);
        await Type(page, "p");
        var evening = await page.EvaluateAsync<string>(Time, "time");
        await page.Keyboard.PressAsync("ArrowUp");

        // 9 can only be nine o'clock, and a morning until the reader says otherwise.
        Assert.Equal("09:30:AM = 09:30", morning);
        Assert.Equal("meridiem", after);
        Assert.Equal("09:30:PM = 21:30", evening);
        Assert.Equal("09:30:AM = 09:30", await page.EvaluateAsync<string>(Time, "time"));
    }

    [Fact]
    public async Task The_parts_follow_a_value_the_page_writes_into_the_hidden_field()
    {
        await using var session = await HookSession.OpenAsync<SegmentsHookPage>(playwright);
        var page = session.Page;

        var rendered = await page.EvaluateAsync<string>(Time, "set");
        await page.EvaluateAsync("() => document.getElementById('date-value').setAttribute('value', '2031-07-04')");
        await page.WaitForFunctionAsync("() => document.querySelector('#date [data-rask-segment=year]').value === '2031'");

        // Rendered with value="15:05".
        Assert.Equal("03:05:PM = 15:05", rendered);
        Assert.Equal("07/04/2031 = 2031-07-04", await page.EvaluateAsync<string>(Date));
    }
}

/// <summary>A typed date and a typed time, written out by hand.</summary>
public sealed partial class SegmentsHookPage : Component
{
    private const string Html = """
        <div id="date" data-rask-segments>
            <input id="date-value" type="hidden">
            <input data-rask-segment="month" placeholder="mm" aria-label="Month" inputmode="numeric" size="2">
            <input data-rask-segment="day" placeholder="dd" aria-label="Day" inputmode="numeric" size="2">
            <input data-rask-segment="year" placeholder="yyyy" aria-label="Year" inputmode="numeric" size="4">
        </div>
        <div id="time" data-rask-segments>
            <input id="time-value" type="hidden">
            <input data-rask-segment="hour" placeholder="--" aria-label="Hour" inputmode="numeric" size="2">
            <input data-rask-segment="minute" placeholder="--" aria-label="Minute" inputmode="numeric" size="2">
            <input data-rask-segment="meridiem" placeholder="--" aria-label="AM or PM" size="2">
        </div>
        <div id="set" data-rask-segments>
            <input type="hidden" value="15:05">
            <input data-rask-segment="hour" placeholder="--" aria-label="Hour" size="2">
            <input data-rask-segment="minute" placeholder="--" aria-label="Minute" size="2">
            <input data-rask-segment="meridiem" placeholder="--" aria-label="AM or PM" size="2">
        </div>
        <button id="elsewhere" type="button">elsewhere</button>
        <div style="height:3000px"></div>
        """;

    protected override Component? HeadAssets => Markup.Title["segment hooks"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Div[Raw.Value(Html)];
}
