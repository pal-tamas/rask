using Rask.Core;
using Rask.Core.Diagnostics;
using static Rask.UiTests.Live.Reports;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Live;

/// <summary>
///     A kit control that writes through a handler of its own — a thumb, a cell, the hidden field a picker's runtime
///     fills, a held list's field, the editor's engine — takes nothing while it is drawn disabled (#1272), and a
///     calendar takes only a day it drew the reader able to press. The browser sends neither, so what arrives was
///     written by hand: nothing is written, and <c>Rask.Forms</c> says so once, naming the control, never the value.
/// </summary>
[Collection(ChoiceOffersCollection.Name)]
public sealed partial class DisabledKitWritesTests : global::Rask.Core.RaskMarkup
{
    private const string HeldField = "[data-rask-list] > input[type=\"hidden\"][data-rask-on-change]";

    private static readonly DateOnly October4 = new(2026, 10, 4);

    [Fact]
    public async Task A_disabled_bound_slider_refuses_a_thumb_moved_by_hand()
    {
        var m = new Model();
        var page = Page.Render(() => Ui.Slider.Bind(() => m.Volume).Live().Disabled());

        var reported = await Reported(() => page.Invoke(page.HandlerIds("input")[0], Sent("80")));

        Assert.Equal(30, m.Volume);
        Refusal(reported, "80");
    }

    [Fact]
    public async Task An_enabled_bound_slider_still_moves()
    {
        var m = new Model();
        var page = Page.Render(() => Ui.Slider.Bind(() => m.Volume).Live());

        var reported = await Reported(() => page.Invoke(page.HandlerIds("input")[0], Sent("80")));

        Assert.Equal(80, m.Volume);
        Assert.DoesNotContain(reported, e => e.Category == "Rask.Forms");
    }

    [Fact]
    public async Task A_disabled_code_field_refuses_a_code_typed_by_hand()
    {
        var code = "";
        var page = Page.Render(() => Ui.Otp.Value(code).OnChange(c => code = c).Length(4).Name("pin").Disabled());

        var reported = await Reported(() => page.Invoke(page.HandlerId("input")!, Sent("4321")));

        Assert.Equal("", code);
        Assert.Contains("'pin'", Refusal(reported, "4321").Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_enabled_code_field_still_takes_a_code()
    {
        var code = "";
        var page = Page.Render(() => Ui.Otp.Value(code).OnChange(c => code = c).Length(4));

        await page.Invoke(page.HandlerId("input")!, Sent("4321"));

        Assert.Equal("4321", code);
    }

    [Fact]
    public async Task A_disabled_date_picker_refuses_a_day_written_by_hand()
    {
        DateOnly day = default;
        var page = Page.Render(() => Ui.DatePicker.Value(day).OnChange(d => day = d).Locale("en-US").Disabled());

        var reported = await Reported(() => page.Invoke(page.HandlerIds("change")[0], Sent("2026-10-04")));

        Assert.Equal(default, day);
        Assert.Contains("Ui.DatePicker", Refusal(reported, "2026-10-04").Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disabled_typed_date_picker_refuses_a_day_typed_by_hand()
    {
        DateOnly day = default;
        var page = Page.Render(() => Ui.DatePicker.Value(day).OnChange(d => day = d).Type(Ui.DatePickerType.Input).Locale("en-US").Disabled());

        var reported = await Reported(async () =>
        {
            foreach (var field in page.HandlerIds("change"))
            {
                await page.TryInvoke(field, Sent("2026-10-04"));
            }
        });

        Assert.Equal(default, day);
        Assert.All(reported, e => Assert.Equal(RaskLogLevel.Warning, e.Level));
        Assert.NotEmpty(reported);
    }

    [Fact]
    public async Task A_date_picker_refuses_a_day_after_its_max()
    {
        DateOnly day = default;
        var page = Page.Render(() => Ui.DatePicker.Value(day).OnChange(d => day = d).Max(new DateOnly(2026, 10, 31)).Locale("en-US"));

        var reported = await Reported(() => page.Invoke(page.HandlerIds("change")[0], Sent("2026-11-05")));

        Assert.Equal(default, day);
        Assert.Contains("none of its choices offered", Refusal(reported, "2026-11-05").Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_date_picker_refuses_an_unavailable_day()
    {
        DateOnly day = default;
        var page = Page.Render(() => Ui.DatePicker.Value(day).OnChange(d => day = d).Unavailable([October4]).Locale("en-US"));

        var reported = await Reported(() => page.Invoke(page.HandlerIds("change")[0], Sent("2026-10-04")));

        Assert.Equal(default, day);
        Refusal(reported, "2026-10-04");
    }

    [Fact]
    public async Task A_date_picker_takes_a_day_within_its_limits()
    {
        DateOnly day = default;
        var page = Page.Render(() => Ui.DatePicker.Value(day).OnChange(d => day = d)
            .Min(new DateOnly(2026, 10, 1)).Max(new DateOnly(2026, 10, 31)).Unavailable([new DateOnly(2026, 10, 5)]).Locale("en-US"));

        var reported = await Reported(() => page.Invoke(page.HandlerIds("change")[0], Sent("2026-10-04")));

        Assert.Equal(October4, day);
        Assert.DoesNotContain(reported, e => e.Category == "Rask.Forms");
    }

    [Fact]
    public async Task A_range_picker_takes_a_preset_its_limits_would_cut()
    {
        UiDateRange range = default;
        var today = UiCalendarOptions.Now();
        var week = UiDateRange.Of(Ui.DateRangePreset.Last7Days, today);
        var page = Page.Render(() => Ui.DatePicker.Range.Value(range).OnChange(r => range = r).WithPresets().Min(today).Locale("en-US"));

        await page.Invoke(page.HandlerIds("change")[0], Sent(UiCalendarValue.Range(week)));

        Assert.Equal(week, range);
    }

    [Fact]
    public async Task A_range_picker_refuses_a_preset_that_does_not_span_its_range()
    {
        UiDateRange range = default;
        var forged = new UiDateRange(new DateOnly(2020, 1, 1), new DateOnly(2030, 1, 1)) { Preset = Ui.DateRangePreset.Last7Days };
        var page = Page.Render(() => Ui.DatePicker.Range.Value(range).OnChange(r => range = r).WithPresets().Locale("en-US"));

        var reported = await Reported(() => page.Invoke(page.HandlerIds("change")[0], Sent(UiCalendarValue.Range(forged))));

        Assert.Equal(default, range);
        Refusal(reported, "2020");
    }

    [Fact]
    public async Task A_disabled_datetime_picker_refuses_a_moment_written_by_hand()
    {
        DateTime moment = default;
        var page = Page.Render(() => Ui.DatetimePicker.Value(moment).OnChange(d => moment = d).Locale("en-US").Disabled());

        var reported = await Reported(() => page.Invoke(page.HandlerIds("change")[0], Sent("2026-10-04T10:30")));

        Assert.Equal(default, moment);
        Assert.Contains("Ui.DatetimePicker", Refusal(reported, "10:30").Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_datetime_picker_refuses_a_day_after_its_max()
    {
        DateTime moment = default;
        var page = Page.Render(() => Ui.DatetimePicker.Value(moment).OnChange(d => moment = d).Max(new DateTime(2026, 10, 31, 18, 0, 0)).Locale("en-US"));

        var reported = await Reported(() => page.Invoke(page.HandlerIds("change")[0], Sent("2026-11-05T10:30")));

        Assert.Equal(default, moment);
        Refusal(reported, "2026-11-05");
    }

    [Fact]
    public async Task An_enabled_datetime_picker_still_takes_a_moment()
    {
        DateTime moment = default;
        var page = Page.Render(() => Ui.DatetimePicker.Value(moment).OnChange(d => moment = d).Locale("en-US"));

        await page.Invoke(page.HandlerIds("change")[0], Sent("2026-10-04T10:30"));

        Assert.Equal(new DateTime(2026, 10, 4, 10, 30, 0), moment);
    }

    [Fact]
    public async Task A_calendar_refuses_a_day_before_its_min()
    {
        DateOnly day = default;
        var page = Page.Render(() => Ui.Calendar.Value(day).OnChange(d => day = d).Min(new DateOnly(2026, 10, 1)).Locale("en-US"));

        var reported = await Reported(() => page.Invoke(page.HandlerIds("change")[0], Sent("2026-09-30")));

        Assert.Equal(default, day);
        Assert.Contains("Ui.Calendar", Refusal(reported, "2026-09-30").Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_calendar_takes_a_day_after_its_min()
    {
        DateOnly day = default;
        var page = Page.Render(() => Ui.Calendar.Value(day).OnChange(d => day = d).Min(new DateOnly(2026, 10, 1)).Locale("en-US"));

        await page.Invoke(page.HandlerIds("change")[0], Sent("2026-10-04"));

        Assert.Equal(October4, day);
    }

    [Fact]
    public async Task A_disabled_time_picker_refuses_a_time_pressed_by_hand()
    {
        var m = new Model();
        var page = Page.Render(() => Ui.TimePicker.Bind(() => m.At).Live().Locale("en-US").Disabled());

        var reported = await Reported(() => page.Invoke(page.HandlerIds("click")[^1], "{}"));

        Assert.Null(m.At);
        Assert.Contains("Ui.TimePicker 'At'", Refusal(reported, "23:30").Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disabled_typed_time_picker_refuses_a_time_typed_by_hand()
    {
        var m = new Model();
        var page = Page.Render(() => Ui.TimePicker.Bind(() => m.At).Live().Type(Ui.TimePickerType.Input).Locale("en-US").Disabled());

        var reported = await Reported(() => page.Invoke(page.HandlerIds("change")[^1], Sent("09:00")));

        Assert.Null(m.At);
        Refusal(reported, "09:00");
    }

    [Fact]
    public async Task An_enabled_time_picker_still_takes_a_time_pressed()
    {
        var m = new Model();
        var page = Page.Render(() => Ui.TimePicker.Bind(() => m.At).Live().Locale("en-US"));

        await page.Invoke(page.HandlerIds("click")[^1], "{}");

        Assert.NotNull(m.At);
    }

    [Fact]
    public async Task A_disabled_held_listbox_refuses_a_pick_written_by_hand()
    {
        var trip = new Trip();
        var page = Acting.Render(() => Ui.Select.Bind(() => trip.Room).Listbox.Disabled()[Rooms()]);

        var reported = await Reported(async () =>
        {
            await page.Invoke(page.Find(HeldField).Attribute("data-rask-on-change")!, Held("attic"));
            await Acting.Act(page);
        });

        Assert.Equal("cellar", trip.Room);
        Assert.Contains("Ui.Select 'Room'", Refusal(reported, "attic").Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disabled_held_combobox_refuses_a_pick_written_by_hand()
    {
        var trip = new Trip();
        var page = Acting.Render(() => Ui.Select.Bind(() => trip.Room).Combobox.Disabled()[Rooms()]);

        var reported = await Reported(async () =>
        {
            await page.Invoke(page.Find(HeldField).Attribute("data-rask-on-change")!, Held("attic"));
            await Acting.Act(page);
        });

        Assert.Equal("cellar", trip.Room);
        Refusal(reported, "attic");
    }

    [Fact]
    public async Task A_held_listbox_keeps_its_answer_when_sent_only_rows_it_never_drew()
    {
        var trip = new Trip();
        var page = Acting.Render(() => Ui.Select.Bind(() => trip.Room).Listbox[Rooms()]);

        var reported = await Reported(async () =>
        {
            await page.Invoke(page.Find(HeldField).Attribute("data-rask-on-change")!, Held("vault", "hall"));
            await Acting.Act(page);
        });

        Assert.Equal("cellar", trip.Room);
        Refusal(reported, "vault");
    }

    [Fact]
    public async Task An_enabled_held_listbox_still_takes_a_pick()
    {
        var trip = new Trip();
        var page = Acting.Render(() => Ui.Select.Bind(() => trip.Room).Listbox[Rooms()]);

        var reported = await Reported(async () =>
        {
            await page.Invoke(page.Find(HeldField).Attribute("data-rask-on-change")!, Held("attic"));
            await Acting.Act(page);
        });

        Assert.Equal("attic", trip.Room);
        Assert.DoesNotContain(reported, e => e.Category == "Rask.Forms");
    }

    [Fact]
    public async Task A_disabled_pillbox_refuses_a_pill_written_by_hand()
    {
        var trip = new Trip();
        var page = Acting.Render(() => Ui.Pillbox.Bind(() => trip.Rooms).Disabled()[Ui.PillboxOption["attic"], Ui.PillboxOption["cellar"]]);

        var reported = await Reported(async () =>
        {
            await page.Invoke(page.Find(HeldField).Attribute("data-rask-on-change")!, Held("attic"));
            await Acting.Act(page);
        });

        Assert.Empty(trip.Rooms);
        Refusal(reported, "attic");
    }

    [Fact]
    public async Task A_disabled_editor_refuses_a_change_its_engine_was_sent()
    {
        var heard = new List<string>();
        var editor = Ui.Editor.Value("<p>kept</p>").OnChange(heard.Add).Disabled();

        var reported = await Reported(() => editor.Changed("<p>forged</p>"));

        Assert.Empty(heard);
        Assert.Contains("Ui.Editor", Refusal(reported, "forged").Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_enabled_editor_still_hears_a_change()
    {
        var heard = new List<string>();
        var editor = Ui.Editor.Value("<p>kept</p>").OnChange(heard.Add);

        await editor.Changed("<p>typed</p>");

        Assert.Equal(["<p>typed</p>"], heard);
    }

    private static string Sent(string value) => "{\"value\":" + System.Text.Json.JsonSerializer.Serialize(value) + "}";

    private static string Held(params string[] values) => Sent(UiFormValues.Write(values));

    private static IEnumerable<Component> Rooms() =>
    [
        Ui.SelectOption.Key("attic").Value("attic")["Attic"],
        Ui.SelectOption.Key("cellar").Value("cellar")["Cellar"],
        Ui.SelectOption.Key("hall").Value("hall").Disabled()["Hall"],
    ];

    private sealed class Model
    {
        public int Volume { get; set; } = 30;

        public TimeOnly? At { get; set; }
    }

    private sealed class Trip
    {
        public string? Room { get; set; } = "cellar";

        public List<string> Rooms { get; set; } = [];
    }
}
