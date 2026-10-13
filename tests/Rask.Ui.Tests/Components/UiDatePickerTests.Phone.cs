using Rask.Testing;
using Rask.UiTests.Flux;

namespace Rask.UiTests.Components;

// The date picker a phone shows: under 640 px Flux's is a sheet over the page that writes nothing until its
// button is pressed. The browser tells the page how wide it is through the box the calendar draws for it.
public partial class UiDatePickerTests
{
    private const string Width = "[data-ui-calendar-narrow] input";

    private const string Selects = "div[role=\"button\"]";

    private const string Closed = "{\"oldState\":\"open\",\"newState\":\"closed\"}";

    [Fact]
    public async Task On_a_phone_a_pick_marks_the_day_and_writes_nothing_until_the_button_is_pressed()
    {
        var model = new Trip();
        var page = Acting.Render(() => Ui.DatePicker.Bind(() => model.Arrival).Locale("en-US").On(Today));

        await page.On(Width).Input("2 1");
        await page.On(Day(20)).Click();
        var (held, marked) = (model.Arrival, page.Html);
        await page.On(Selects).Click();
        await Acting.Act(page);

        Assert.Equal(default, held);
        Assert.Matches("data-selected[^>]*data-date=\"2026-01-20\"|data-date=\"2026-01-20\"[^>]*data-selected", marked);
        Assert.Contains(">Select a date</span>", marked, StringComparison.Ordinal);
        Assert.Equal(Jan(20), model.Arrival);
    }

    [Fact]
    public async Task On_a_phone_no_pick_closes_the_popup()
    {
        var page = Page.Render(() => Single(default));

        var wide = page.Html;
        await page.On(Width).Input("2 1");

        Assert.Contains("popovertargetaction=\"hide\"", wide, StringComparison.Ordinal);
        Assert.DoesNotContain("popovertargetaction", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_a_phone_a_closed_sheet_leaves_the_value_and_keeps_its_mark_as_flux_does()
    {
        var model = new Trip();
        var page = Acting.Render(() => Ui.DatePicker.Bind(() => model.Arrival).Locale("en-US").On(Today));

        await page.On(Width).Input("2 1");
        await page.On(Day(22)).Click();
        await page.On("[popover]").Raise("toggle", Closed);
        var cancelled = model.Arrival;
        await page.On(Selects).Click();
        await Acting.Act(page);

        Assert.Equal(default, cancelled);
        Assert.Equal(Jan(22), model.Arrival);
    }

    [Fact]
    public async Task Back_on_a_wide_window_a_pick_writes_at_once_again()
    {
        var model = new Trip();
        var page = Acting.Render(() => Ui.DatePicker.Bind(() => model.Arrival).Locale("en-US").On(Today));

        await page.On(Width).Input("2 1");
        await page.On(Width).Input("1 1");
        await page.On(Day(20)).Click();
        await Acting.Act(page);

        Assert.Equal(Jan(20), model.Arrival);
    }

    [Fact]
    public async Task On_a_phone_a_range_draws_one_month_and_waits_for_select_range()
    {
        var model = new Trip();
        var page = Acting.Render(() => Ui.DatePicker.Range.Bind(() => model.Stay).Locale("en-US").On(Today));

        var wide = page.FindAll("[data-month]").Count;
        await page.On(Width).Input("2 1");
        var narrow = page.FindAll("[data-month]").Count;
        await page.On(Day(20)).Click();
        await page.On(Day(23)).Click();
        var held = model.Stay;
        await page.On(Selects).Click();
        await Acting.Act(page);

        Assert.Equal((2, 1), (wide, narrow));
        Assert.Equal(default, held);
        Assert.Equal(new UiDateRange(Jan(20), Jan(23)), model.Stay);
        Assert.Contains(">Select range</button>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_buttons_of_the_sheet_are_drawn_for_a_phone_and_for_a_picker_that_confirms()
    {
        var plain = Single(default).ToHtml();

        var confirming = Single(default).WithConfirmation().ToHtml();

        Assert.Contains("hidden max-sm:flex justify-end", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden max-sm:flex", confirming, StringComparison.Ordinal);
        Assert.Contains(">Select date</button>", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void The_popup_is_left_to_the_stylesheet_on_a_phone_and_hangs_from_the_picker_elsewhere()
    {
        var html = Single(default).ToHtml();

        var dialog = System.Text.RegularExpressions.Regex.Match(html, "<dialog[^>]*>").Value;

        Assert.Contains("max-sm:[--rask-place:none]", dialog, StringComparison.Ordinal);
        Assert.Contains("sm:top-[anchor(bottom)]", dialog, StringComparison.Ordinal);
        Assert.Contains("max-sm:backdrop:bg-black/10", dialog, StringComparison.Ordinal);
        Assert.Contains("data-rask-place=\"bottom start 5 0\"", dialog, StringComparison.Ordinal);
        Assert.Matches("style=\"position-anchor:--ui-date-picker-\\d+\"", dialog);
    }

    [Fact]
    public async Task On_a_phone_the_presets_are_a_select_and_one_chosen_there_waits_for_the_button_too()
    {
        var model = new Trip();
        var page = Acting.Render(() => Ui.DatePicker.Range.Bind(() => model.Stay).WithPresets().Locale("en-US").On(Today));

        await page.On(Width).Input("2 1");
        await page.On("select[data-ui-select-native]").Change("yesterday");
        var held = model.Stay;
        await page.On(Selects).Click();
        await Acting.Act(page);

        Assert.Equal(default, held);
        Assert.Equal(Jan(14), model.Stay.Start);
        Assert.Equal(Ui.DateRangePreset.Yesterday, model.Stay.Preset);
        Assert.Contains("<option disabled value=\"\">Choose predefined range...</option>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_a_calendar_that_would_change_asks_how_wide_the_window_is()
    {
        var one = Ui.Calendar.Value(default(DateOnly)).Locale("en-US").On(Today).ToHtml();

        var two = Ui.Calendar.Value(default(DateOnly)).Months(2).Locale("en-US").On(Today).ToHtml();

        Assert.DoesNotContain("data-rask-measure", one, StringComparison.Ordinal);
        Assert.Contains("data-rask-measure", two, StringComparison.Ordinal);
        Assert.Contains("data-month=\"\" class=\"max-sm:hidden\"", two.Replace("class=\"max-sm:hidden\" data-month=\"\"", "data-month=\"\" class=\"max-sm:hidden\"", StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
