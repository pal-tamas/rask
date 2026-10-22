using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The date picker both apps put on every form: typed into by hand, with the month and the year chosen from the
///     header's lists — <c>.SelectableHeader().Trigger(Ui.DatePickerInput)</c>, bound and not live — low in a
///     1280 × 800 window. A month chosen from a list is a page like the arrows': it keeps the open calendar's side
///     and sends nothing; the calendar reopens on it; a date typed while it is shut is the one it opens on.
/// </summary>
public sealed class TypedHeaderDatePickerTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Root = "[data-ui-date-picker]:has(#fulfilled)";

    private const string Popup = Root + " [popover]";

    private const string Frames = "() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(() => requestAnimationFrame(done))))";

    private const string Top = "s => Math.round(document.querySelector(s).getBoundingClientRect().top)";

    // The day in the middle of what the calendar shows, which names its month.
    private const string Month = "s => document.querySelector(s + ' td[data-date$=\"-15\"]:not([data-outside])').dataset.date";

    private async Task<(HookSession Session, SocketFrames Frames)> OpenAsync()
    {
        SocketFrames? frames = null;
        var session = await HookSession.OpenAsync<TypedHeaderDatePage>(
            playwright, new() { Locale = "hu-HU", ViewportSize = new() { Width = 1280, Height = 800 } },
            beforeLoad: opened =>
            {
                frames = SocketFrames.Of(opened);
                return Task.CompletedTask;
            },
            cultures: o => o.SupportedCultures.Add("hu"));
        await session.HooksLoadedAsync();
        await session.Page.EvaluateAsync("s => window.scrollBy(0, document.querySelector(s).getBoundingClientRect().bottom - 466)", Root);
        return (session, frames!);
    }

    private static async Task TypeAsync(IPage page, string digits)
    {
        await page.Locator("#fulfilled [data-rask-segment=\"year\"]").ClickAsync();
        await page.Keyboard.TypeAsync(digits);
    }

    private static async Task OpenCalendarAsync(IPage page)
    {
        await page.Locator("#fulfilled > svg").Last.ClickAsync();
        await Expect(page.Locator(Popup)).ToBeVisibleAsync();
        await page.EvaluateAsync(Frames);
    }

    private static async Task ChooseAsync(IPage page, string part, string value, string day)
    {
        await page.Locator(Popup + $" [data-rask-calendar-part={part}]").SelectOptionAsync(value);
        await Expect(page.Locator(Popup + $" td[data-date=\"{day}\"]:not([data-outside])")).ToHaveCountAsync(1);
        await page.EvaluateAsync(Frames);
    }

    [Fact]
    public async Task A_month_and_a_year_chosen_from_the_header_keep_the_side_send_nothing_and_are_where_it_reopens()
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;
        await TypeAsync(page, "20261015");
        await OpenCalendarAsync(page);
        var opened = await page.EvaluateAsync<int>(Top, Popup);
        var sentBefore = frames.Sent;

        await ChooseAsync(page, "month", "8", "2026-08-15");
        var august = await page.EvaluateAsync<int>(Top, Popup);
        await ChooseAsync(page, "year", "2027", "2027-08-15");
        var nextAugust = await page.EvaluateAsync<int>(Top, Popup);
        var sentByChoosing = frames.Sent - sentBefore;
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator(Popup)).ToBeHiddenAsync();
        await OpenCalendarAsync(page);
        var reopened = await page.EvaluateAsync<string>(Month, Popup);

        // October has five weeks and fits under the field; August, six, would not, and stays there all the same.
        Assert.Equal(471, opened);
        Assert.Equal(opened, august);
        Assert.Equal(opened, nextAugust);
        Assert.Equal(0, sentByChoosing);
        Assert.Equal("2027-08-15", reopened);
    }

    [Fact]
    public async Task A_date_typed_while_the_calendar_is_shut_is_the_one_it_opens_on_and_the_typed_date_is_saved()
    {
        var (session, _) = await OpenAsync();
        await using var __ = session;
        var page = session.Page;
        await TypeAsync(page, "20261015");
        await OpenCalendarAsync(page);
        await ChooseAsync(page, "month", "8", "2026-08-15");
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator(Popup)).ToBeHiddenAsync();

        await TypeAsync(page, "20260320");
        await OpenCalendarAsync(page);
        var marked = await page.Locator(Popup + " td[data-selected]").EvaluateAllAsync<string[]>("cells => cells.map(c => c.dataset.date)");
        await page.Keyboard.PressAsync("Escape");
        await page.ClickAsync("#save");

        Assert.Equal(["2026-03-20"], marked);
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=2026-03-20");
    }

    [Fact]
    public async Task A_range_typed_into_its_two_fields_is_the_range_its_calendar_opens_on()
    {
        var (session, _) = await OpenAsync();
        await using var __ = session;
        var page = session.Page;
        var range = page.Locator("[data-ui-date-picker]").First;

        await range.Locator("[data-rask-segment=\"year\"]").First.ClickAsync();
        await page.Keyboard.TypeAsync("20260803");
        await range.Locator("[data-rask-segment=\"year\"]").Nth(1).ClickAsync();
        await page.Keyboard.TypeAsync("20260807");
        await range.Locator("[data-rask-toggle] > svg").Last.ClickAsync();
        await Expect(range.Locator("[popover]")).ToBeVisibleAsync();

        await Expect(range.Locator("[popover] td[data-in-range]:not([data-outside])")).ToHaveCountAsync(5);
        await Expect(range.Locator("[popover] td[data-start]")).ToHaveAttributeAsync("data-date", "2026-08-03");
    }
}

/// <summary>A form with the apps' date pickers: one low in the window, and a range typed into two fields above it.</summary>
public sealed partial class TypedHeaderDatePage : Component
{
    private readonly Delivery _delivery = new();
    private string _saved = "";

    protected override Component? HeadAssets => [Markup.Title["typed header date"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "hu";

    protected override Component? Render() =>
    [
        P.Id("saved")[$"saved={_saved}"],
        Form.Model(_delivery).OnSubmit(d => _saved = d.Fulfilled?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "")[
            Ui.DatePicker.Range.Bind(() => _delivery.Stay).Locale("hu").SelectableHeader().Id("stay").Label("Időszak")
                .Trigger(Div[Ui.DatePickerInput, Ui.DatePickerInput]),
            Div.Style("height:900px"),
            Ui.DatePicker.Bind(() => _delivery.Fulfilled).Locale("hu").Clearable(true).SelectableHeader().Id("fulfilled")
                .Label("Teljesítve").Trigger(Ui.DatePickerInput),
            Ui.Button.Submit.Id("save")["Mentés"]
        ],
        Div.Style("height:2000px")
    ];

    private sealed class Delivery
    {
        public DateOnly? Fulfilled { get; set; }

        public UiDateRange Stay { get; set; }
    }
}
