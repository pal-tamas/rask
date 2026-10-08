using System.Globalization;
using System.Text.RegularExpressions;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>Flux UI's time picker: what it binds, what it lists, and what the keyboard and the pointer do to it.</summary>
public partial class UiTimePickerTests : global::Rask.Core.RaskMarkup
{
    private const string Opened = "{\"oldState\":\"closed\",\"newState\":\"open\"}";

    [Fact]
    public void A_time_picker_is_a_combobox_button_over_a_listbox_of_times()
    {
        var html = Ui.TimePicker.Of<TimeOnly?>().Locale("en-US").ToHtml();

        var options = Regex.Count(html, "role=\"option\"");

        Assert.Equal(48, options);
        Assert.Contains("role=\"combobox\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-haspopup=\"listbox\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("popover=\"auto\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"listbox\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-multiselectable=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-time-picker", html, StringComparison.Ordinal);
        Assert.Contains("Select a time", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(60, 24)]
    [InlineData(15, 96)]
    [InlineData(45, 32)]
    public void The_interval_decides_how_many_times_are_listed(int interval, int expected)
    {
        var html = Ui.TimePicker.Of<TimeOnly?>().Interval(interval).ToHtml();

        var options = Regex.Count(html, "role=\"option\"");

        Assert.Equal(expected, options);
    }

    [Fact]
    public void Min_and_max_are_the_first_and_the_last_time_listed()
    {
        var html = Ui.TimePicker.Of<TimeOnly?>().Min(new TimeOnly(9, 0)).Max(new TimeOnly(17, 0)).ToHtml();

        var times = Regex.Matches(html, "data-time=\"([0-9:]+)\"").Select(match => match.Groups[1].Value).ToList();

        Assert.Equal(17, times.Count);
        Assert.Equal("09:00", times[0]);
        Assert.Equal("17:00", times[^1]);
    }

    [Fact]
    public void An_unavailable_time_is_listed_and_cannot_be_picked()
    {
        var html = Ui.TimePicker.Of<TimeOnly?>()
            .Unavailable([new TimeOnly(3, 0), new UiTimeRange(new TimeOnly(5, 30), new TimeOnly(7, 29))])
            .ToHtml();

        var disabled = Regex.Matches(html, "<button[^>]*data-time=\"([0-9:]+)\"[^>]*disabled")
            .Select(match => match.Groups[1].Value)
            .ToList();

        Assert.Equal(["03:00", "05:30", "06:00", "06:30", "07:00"], disabled);
        Assert.Equal(48, Regex.Count(html, "role=\"option\""));
    }

    [Fact]
    public async Task A_pick_writes_the_bound_time_and_says_so_on_the_button()
    {
        var model = new Booking();
        var page = Page.Render(() => Ui.TimePicker.Bind(() => model.At).Locale("en-US").Label("Starts at"));

        await page.On("[data-time=\"09:30\"]").Click();

        Assert.Equal(new TimeOnly(9, 30), model.At);
        Assert.Contains("<div dir=\"auto\">9:30 AM</div>", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Select a time", page.Html, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(page.Html, "aria-selected=\"true\""));
    }

    [Fact]
    public void A_single_pick_closes_the_list_on_the_same_press()
    {
        var html = Ui.TimePicker.Of<TimeOnly?>().ToHtml();

        var closing = Regex.Count(html, "popovertargetaction=\"hide\"");

        Assert.Equal(48, closing);
    }

    [Fact]
    public async Task A_second_press_on_the_chosen_time_takes_it_back()
    {
        var model = new Booking { At = new TimeOnly(9, 30) };
        var page = Page.Render(() => Ui.TimePicker.Bind(() => model.At));

        await page.On("[data-time=\"09:30\"]").Click();

        Assert.Null(model.At);
        Assert.Contains("Select a time", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_time_that_cannot_be_empty_stays_chosen()
    {
        var model = new Booking();
        var page = Page.Render(() => Ui.TimePicker.Bind(() => model.Opens));

        await page.On("[data-time=\"08:00\"]").Click();

        Assert.Equal(new TimeOnly(8, 0), model.Opens);
        Assert.Equal(1, Regex.Count(page.Html, "aria-selected=\"true\""));
    }

    [Fact]
    public async Task A_collection_of_times_picks_several_and_keeps_the_list_open()
    {
        var model = new Booking();
        var page = Page.Render(() => Ui.TimePicker.Bind(() => model.Slots).Locale("en-US"));

        await page.On("[data-time=\"10:30\"]").Click();
        await page.On("[data-time=\"09:00\"]").Click();

        Assert.Equal([new TimeOnly(9, 0), new TimeOnly(10, 30)], model.Slots);
        Assert.Contains("9:00 AM, 10:30 AM", page.Html, StringComparison.Ordinal);
        Assert.Contains("aria-multiselectable=\"true\"", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("popovertargetaction", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_press_on_one_of_several_chosen_times_takes_only_that_one_back()
    {
        var model = new Booking();
        model.Kept.Add(new TimeOnly(9, 0));
        model.Kept.Add(new TimeOnly(10, 30));
        var page = Page.Render(() => Ui.TimePicker.Bind(() => model.Kept));

        await page.On("[data-time=\"09:00\"]").Click();

        Assert.Equal([new TimeOnly(10, 30)], model.Kept);
    }

    [Fact]
    public async Task A_controlled_picker_hands_the_parent_the_shape_it_was_given()
    {
        TimeOnly[] times = [new TimeOnly(9, 0)];
        var page = Page.Render(() => Ui.TimePicker.Value(times).OnChange(changed => times = changed));

        await page.On("[data-time=\"13:00\"]").Click();

        Assert.Equal([new TimeOnly(9, 0), new TimeOnly(13, 0)], times);
    }

    [Theory]
    [InlineData("en-US", "1:30 PM")]
    [InlineData("de-DE", "13:30")]
    [InlineData("ja-JP", "13:30")]
    public void The_culture_decides_between_twelve_and_twenty_four_hours(string locale, string expected)
    {
        var html = Ui.TimePicker.Of<TimeOnly?>().Locale(locale).ToHtml();

        var written = Regex.Match(html, "data-time=\"13:30\".*?<div dir=\"ltr\">([^<]+)</div>").Groups[1].Value;

        Assert.Equal(expected, written);
    }

    [Fact]
    public void The_app_s_current_culture_is_the_one_used_when_none_is_named()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        var html = Ui.TimePicker.Of<TimeOnly?>().ToHtml();
        CultureInfo.CurrentCulture = before;

        Assert.Contains("<div dir=\"ltr\">13:30</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_time_format_overrides_the_culture()
    {
        var twelve = Ui.TimePicker.Of<TimeOnly?>().Locale("de-DE").TwelveHour.ToHtml();
        var twentyFour = Ui.TimePicker.Of<TimeOnly?>().Locale("en-US").TwentyFourHour.ToHtml();

        var midnight = Regex.Match(twentyFour, "data-time=\"00:00\".*?<div dir=\"ltr\">([^<]+)</div>").Groups[1].Value;

        Assert.Contains("<div dir=\"ltr\">1:30 PM</div>", twelve, StringComparison.Ordinal);
        Assert.Equal("00:00", midnight);
    }

    [Fact]
    public async Task An_arrow_opens_the_list_and_the_arrows_move_a_cursor_the_button_names()
    {
        var page = Page.Render(() => Ui.TimePicker.Of<TimeOnly?>().Unavailable([new TimeOnly(0, 30)]));

        await page.On("[role=\"combobox\"]").Raise("keydown", "{\"key\":\"ArrowDown\"}");
        var opened = page.Html;
        await page.On("[popover]").Raise("toggle", Opened);
        await page.On("[role=\"combobox\"]").Raise("keydown", "{\"key\":\"ArrowDown\"}");

        Assert.Contains("data-rask-popover-open=\"true\"", opened, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"true\"", page.Html, StringComparison.Ordinal);
        Assert.Contains("aria-activedescendant=\"" + Cursor(page.Html) + "\"", page.Html, StringComparison.Ordinal);
        Assert.Equal("01:00", ActiveTime(page.Html));
    }

    [Fact]
    public async Task Enter_picks_the_time_under_the_cursor_and_closes_the_list()
    {
        var model = new Booking();
        var page = Page.Render(() => Ui.TimePicker.Bind(() => model.At));
        await page.On("[popover]").Raise("toggle", Opened);

        await page.On("[role=\"combobox\"]").Raise("keydown", "{\"key\":\"End\"}");
        await page.On("[role=\"combobox\"]").Raise("keydown", "{\"key\":\"Enter\"}");

        Assert.Equal(new TimeOnly(23, 30), model.At);
        Assert.Contains("data-rask-popover-open=\"false\"", page.Html, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_list_opens_on_the_chosen_time_or_where_it_was_told_to()
    {
        var chosen = Ui.TimePicker.Value<TimeOnly?>(new TimeOnly(14, 0)).OpenTo(new TimeOnly(10, 0)).ToHtml();
        var told = Ui.TimePicker.Of<TimeOnly?>().OpenTo(new TimeOnly(10, 0)).ToHtml();
        var neither = Ui.TimePicker.Of<TimeOnly?>().ToHtml();

        var cursors = new[] { chosen, told, neither }.Select(ActiveTime).ToList();

        Assert.Equal(["14:00", "10:00", "00:00"], cursors);
    }

    [Fact]
    public void A_typed_trigger_is_three_named_fields_beside_the_list()
    {
        var html = Ui.TimePicker.Of<TimeOnly?>().Type(Ui.TimePickerType.Input).Locale("en-US").ToHtml();

        var fields = Regex.Matches(html, "<input[^>]*aria-label=\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToList();

        Assert.Equal(["Hour", "Minute", "AM/PM"], fields);
        Assert.DoesNotContain("role=\"combobox\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"listbox\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_button_is_a_listbox_button_and_its_list_holds_the_page_still()
    {
        var html = Ui.TimePicker.Of<TimeOnly?>().ToHtml();

        // Closed, Enter does nothing and the arrows do not scroll; open, the page neither scrolls nor takes the pointer.
        Assert.Matches("<button[^>]*role=\"combobox\"[^>]*data-rask-listbox-button", html);
        Assert.Matches("role=\"listbox\"[^>]*data-rask-lock=\"\"|data-rask-lock=\"\"[^>]*role=\"listbox\"", html);
    }

    [Fact]
    public void Two_pickers_with_no_label_and_no_binding_do_not_share_an_id()
    {
        var html = Div[Ui.TimePicker.Of<TimeOnly?>(), Ui.TimePicker.Of<TimeOnly?>()].ToHtml();

        var ids = Regex.Matches(html, "<button id=\"(f-field-\\d+)\"").Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(2, ids.Length);
        Assert.Equal(2, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_twenty_four_hour_typed_trigger_has_no_period_field()
    {
        var html = Ui.TimePicker.Of<TimeOnly?>().Type(Ui.TimePickerType.Input).TwentyFourHour.ToHtml();

        var parts = Regex.Matches(html, "data-rask-segment=\"(\\w+)\"").Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(["hour", "minute"], parts);
    }

    [Fact]
    public void The_typed_parts_are_the_runtimes_segments_around_one_hidden_field_that_carries_the_time()
    {
        var html = Ui.TimePicker.Value<TimeOnly?>(new TimeOnly(21, 5)).Type(Ui.TimePickerType.Input).Locale("en-US").ToHtml();

        var parts = Regex.Matches(html, "<input[^>]*data-rask-segment=\"(\\w+)\"[^>]*>").ToArray();

        // A part has no value of its own: the runtime fills it from the hidden field, and owns what is typed.
        Assert.Contains("data-rask-segments=\"\"", html, StringComparison.Ordinal);
        Assert.Equal(["hour", "minute", "meridiem"], parts.Select(m => m.Groups[1].Value));
        Assert.DoesNotContain(parts, part => part.Value.Contains("value=", StringComparison.Ordinal));
        Assert.Matches("<input[^>]*type=\"hidden\"[^>]*value=\"21:05\"|<input[^>]*value=\"21:05\"[^>]*type=\"hidden\"", html);
    }

    [Fact]
    public async Task A_whole_time_in_the_hidden_field_is_the_typed_time_and_an_unavailable_one_is_not_taken()
    {
        var model = new Booking();
        var page = Page.Render(() => Ui.TimePicker.Bind(() => model.At).Type(Ui.TimePickerType.Input).Locale("en-US")
            .Unavailable([new TimeOnly(3, 0)]));

        await page.On("[data-rask-segments] input[type=\"hidden\"]").Change(string.Empty);
        var unfinished = model.At;
        await page.On("[data-rask-segments] input[type=\"hidden\"]").Change("21:05");
        var evening = model.At;
        await page.On("[data-rask-segments] input[type=\"hidden\"]").Change("03:00");

        Assert.Null(unfinished);
        Assert.Equal(new TimeOnly(21, 5), evening);
        Assert.Equal(new TimeOnly(21, 5), model.At);
    }

    [Fact]
    public void Everything_in_the_typed_trigger_but_a_field_opens_the_list_and_none_does_without_a_dropdown()
    {
        var with = Ui.TimePicker.Of<TimeOnly?>().Type(Ui.TimePickerType.Input).ToHtml();

        var without = Ui.TimePicker.Of<TimeOnly?>().Type(Ui.TimePickerType.Input).Dropdown(false).ToHtml();

        // The clock, the room beside the fields and the chevron; a press in a field only puts the caret there.
        Assert.Equal(3, Regex.Matches(with, "data-rask-toggle=\"uitp-\\d+\"").Count);
        Assert.DoesNotMatch("<input[^>]*data-rask-toggle", with);
        Assert.DoesNotContain("data-rask-toggle", without, StringComparison.Ordinal);
    }

    [Fact]
    public void A_typed_trigger_without_its_dropdown_has_no_chevron_and_no_listbox()
    {
        var with = Ui.TimePicker.Of<TimeOnly?>().Type(Ui.TimePickerType.Input).ToHtml();
        var without = Ui.TimePicker.Of<TimeOnly?>().Type(Ui.TimePickerType.Input).Dropdown(false).ToHtml();

        // Forty-eight check marks, the clock, and the chevron where there is a list to open.
        var icons = (Regex.Count(with, "data-ui-icon"), Regex.Count(without, "data-ui-icon"));

        Assert.Equal((50, 49), icons);
        Assert.DoesNotContain("role=\"listbox\"", without, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_and_a_description_draw_the_field_around_the_picker()
    {
        var html = Ui.TimePicker.Of<TimeOnly?>().Label("Starts at").Description("Local time.").Badge("Required").ToHtml();

        var labelled = Regex.IsMatch(html, "<label[^>]*for=\"f-starts-at\"");

        Assert.True(labelled);
        Assert.Contains("<button id=\"f-starts-at\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"f-starts-at-description\"", html, StringComparison.Ordinal);
        Assert.Contains("Local time.", html, StringComparison.Ordinal);
        Assert.Contains("Required", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-field", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_picker_says_so_and_a_disabled_one_cannot_be_opened()
    {
        var invalid = Ui.TimePicker.Of<TimeOnly?>().Invalid().ToHtml();
        var disabled = Ui.TimePicker.Of<TimeOnly?>().Disabled().ToHtml();

        var button = Regex.Match(disabled, "<button id=[^>]*>").Value;

        Assert.Contains("aria-invalid=\"true\"", invalid, StringComparison.Ordinal);
        Assert.Contains("data-invalid", invalid, StringComparison.Ordinal);
        Assert.Contains("disabled", button, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_clearable_picker_shows_its_button_only_while_a_time_is_chosen()
    {
        var model = new Booking { At = new TimeOnly(9, 30) };
        var page = Page.Render(() => Ui.TimePicker.Bind(() => model.At).Clearable());
        var before = page.Html;

        await page.On("[aria-label=\"Clear\"]").Click();

        Assert.Contains("aria-label=\"Clear\"", before, StringComparison.Ordinal);
        Assert.Null(model.At);
        Assert.DoesNotContain("aria-label=\"Clear\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_placeholder_replaces_the_words_on_an_empty_button()
    {
        var html = Ui.TimePicker.Of<TimeOnly?>().Placeholder("Pick a slot").ToHtml();

        var placeholder = Regex.Match(html, "data-ui-time-picker-placeholder>([^<]+)<").Groups[1].Value;

        Assert.Equal("Pick a slot", placeholder);
    }

    private static string ActiveTime(string html) =>
        Regex.Match(html, "data-time=\"([0-9:]+)\"[^>]*data-active").Groups[1].Value;

    private static string Cursor(string html) =>
        Regex.Match(html, "<button id=\"([^\"]+)\"[^>]*data-active[ >]").Groups[1].Value;

    private sealed class Booking
    {
        public TimeOnly? At { get; set; }

        public TimeOnly Opens { get; set; } = new(8, 0);

        public List<TimeOnly> Slots { get; set; } = [];

        public HashSet<TimeOnly> Kept { get; } = [];
    }
}
