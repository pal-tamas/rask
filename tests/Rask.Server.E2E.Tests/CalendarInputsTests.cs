using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     Flux's <c>with-inputs</c> and the native <c>flux:date-picker.input</c>: a date typed over the months is chosen
///     in them, a day picked in them is written over them, and the browser's own date field reads and fills as the
///     typed parts do — all in the browser, with nothing sent until the form is.
/// </summary>
public sealed class CalendarInputsTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private static ILocator Popup(IPage page, string id) => page.Locator($"[data-ui-date-picker]:has(#{id}) [popover]");

    private static ILocator Inputs(IPage page, string id) => Popup(page, id).Locator("[data-rask-calendar-part=inputs]");

    private async Task<(HookSession Session, SocketFrames Frames)> OpenAsync()
    {
        SocketFrames? frames = null;
        var session = await HookSession.OpenAsync<CalendarInputsPage>(playwright, beforeLoad: opened =>
        {
            frames = SocketFrames.Of(opened);
            return Task.CompletedTask;
        });
        await session.HooksLoadedAsync();
        return (session, frames!);
    }

    private static async Task OpenAsync(IPage page, string id)
    {
        await page.Locator($"#{id}").ClickAsync();
        await Expect(Popup(page, id)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_date_typed_over_the_months_is_chosen_in_them_and_sends_nothing()
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;
        await OpenAsync(page, "arrival");
        var sentBefore = frames.Sent;

        await Inputs(page, "arrival").Locator("[data-rask-segment=month]").ClickAsync();
        await page.Keyboard.TypeAsync("08202026");

        await Expect(Popup(page, "arrival").Locator("td[data-selected]:not([data-outside])")).ToHaveAttributeAsync("data-date", "2026-08-20");
        await Expect(Popup(page, "arrival")).ToBeVisibleAsync();
        Assert.Equal(0, frames.Sent - sentBefore);
    }

    [Fact]
    public async Task A_day_picked_in_the_months_is_written_over_them()
    {
        var (session, _) = await OpenAsync();
        await using var __ = session;
        var page = session.Page;
        await OpenAsync(page, "arrival");

        await Popup(page, "arrival").Locator("td[data-date=\"2026-03-20\"] button").ClickAsync();
        await OpenAsync(page, "arrival");

        var inputs = Inputs(page, "arrival");
        await Expect(inputs.Locator("[data-rask-segment=month]")).ToHaveValueAsync("03");
        await Expect(inputs.Locator("[data-rask-segment=day]")).ToHaveValueAsync("20");
        await Expect(inputs.Locator("[data-rask-segment=year]")).ToHaveValueAsync("2026");
    }

    [Fact]
    public async Task A_range_typed_into_the_browsers_date_fields_is_the_range_marked()
    {
        var (session, _) = await OpenAsync();
        await using var __ = session;
        var page = session.Page;
        await OpenAsync(page, "stay");
        var fields = Inputs(page, "stay").Locator("input[type=date]");

        await fields.Nth(0).FillAsync("2026-08-03");
        await fields.Nth(1).FillAsync("2026-08-07");

        await Expect(Popup(page, "stay").Locator("td[data-start]")).ToHaveAttributeAsync("data-date", "2026-08-03");
        await Expect(Popup(page, "stay").Locator("td[data-in-range]:not([data-outside])")).ToHaveCountAsync(5);
    }

    [Fact]
    public async Task A_date_typed_into_a_native_trigger_is_the_one_its_calendar_opens_on_and_saved_with_the_form()
    {
        var (session, _) = await OpenAsync();
        await using var __ = session;
        var page = session.Page;
        var field = page.Locator("#due input[type=date]");

        await field.FillAsync("2026-11-05");
        await page.Locator("#due > svg").Last.ClickAsync();
        await Expect(Popup(page, "due")).ToBeVisibleAsync();
        var marked = await Popup(page, "due").Locator("td[data-selected]:not([data-outside])").GetAttributeAsync("data-date");
        await page.Keyboard.PressAsync("Escape");
        await page.ClickAsync("#save");

        Assert.Equal("2026-11-05", marked);
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=2026-11-05");
    }

    [Fact]
    public async Task A_calendar_with_inputs_chooses_what_is_typed_over_it()
    {
        var (session, _) = await OpenAsync();
        await using var __ = session;
        var page = session.Page;
        var calendar = page.Locator("#days");

        await calendar.Locator("[data-rask-calendar-part=inputs] [data-rask-segment=month]").ClickAsync();
        await page.Keyboard.TypeAsync("02142027");

        await Expect(calendar.Locator("td[data-selected]:not([data-outside])")).ToHaveAttributeAsync("data-date", "2027-02-14");
    }
}

/// <summary>A form with a picker typed over its months, a range typed into the browser's fields, and a native trigger.</summary>
public sealed partial class CalendarInputsPage : Component
{
    private readonly Booking _booking = new();
    private string _saved = "";

    protected override Component? HeadAssets => [Markup.Title["calendar inputs"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override Component? Render() =>
    [
        P.Id("saved")[$"saved={_saved}"],
        Form.Model(_booking).OnSubmit(b => _saved = b.Due?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "")[
            Ui.DatePicker.Bind(() => _booking.Arrival).WithInputs().Locale("en-US").Id("arrival"),
            Ui.DatePicker.Range.Bind(() => _booking.Stay).WithInputs(Ui.DatePickerInputVariant.Native).Locale("en-US").Id("stay"),
            Ui.DatePicker.Bind(() => _booking.Due).Locale("en-US").Id("due").Trigger(Ui.DatePickerInput.Native),
            Ui.Button.Submit.Id("save")["Save"]
        ],
        Ui.Calendar.Value(default(DateOnly)).WithInputs().Locale("en-US").Id("days")
    ];

    private sealed class Booking
    {
        public DateOnly? Arrival { get; set; } = new(2026, 3, 1);

        public UiDateRange Stay { get; set; }

        public DateOnly? Due { get; set; }
    }
}
