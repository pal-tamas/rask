using System.Globalization;
using Rask.Core;
using Rask.UiTests.Flux;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     When the kit's chosen controls write: with the next action unless they say <c>.Live()</c> — Flux's
///     <c>wire:model</c> and <c>wire:model.live</c>. A control of one bound element waits in the browser, as
///     <c>Ui.Input</c> does. One the kit draws still hears its own event, to draw the pick, and the write, the
///     rules and <c>AfterBind</c> wait in the session until a handler that is not such a control's own.
/// </summary>
public partial class UiChosenBindTimingTests : global::Rask.Core.RaskMarkup
{
    // The one field a calendar's page hears the choice through: the browser picks (rask-calendar.ts).
    private const string CalendarField = "[data-ui-calendar] > input[type=\"hidden\"]";

    private static readonly DateOnly Today = new(2026, 1, 15);

    private sealed class Booking
    {
        public bool Agreed { get; set; }

        public bool Notify { get; set; }

        public string Plan { get; set; } = "";

        public string Room { get; set; } = "";

        public DateOnly Day { get; set; }

        public List<string> Extras { get; set; } = [];

        public string Code { get; set; } = "";
    }

    private static string Day(int day) =>
        "button[aria-label=\"" + new DateOnly(2026, 1, day).ToString("D", CultureInfo.GetCultureInfo("en-US")) + "\"]";

    private static IEnumerable<string> NotTheBasement(string room) => room == "Basement" ? ["Not the basement."] : [];

    private static Component Rooms(Booking m, bool? live = null, Action<string>? after = null) =>
        Ui.Select.Bind(() => m.Room).Listbox.Live(live).Validate(NotTheBasement).AfterBind(room => after?.Invoke(room)).Label("Room")[
            Ui.SelectOption.Key("Attic")["Attic"],
            Ui.SelectOption.Key("Basement")["Basement"]
        ];

    private static async Task Pick(Page page, string room)
    {
        await page.On($"[data-ui-option]:has-text(\"{room}\")").Click();
    }

    [Fact]
    public void A_checkbox_a_switch_and_a_native_select_wait_in_the_browser_as_an_input_does()
    {
        var m = new Booking();

        var page = Page.Render(() => Form.Model(m)[
            Ui.Checkbox.Bind(() => m.Agreed).Label("Agreed"),
            Ui.Switch.Bind(() => m.Notify).Label("Notify"),
            Ui.Select.Bind(() => m.Plan).Label("Plan")[Ui.SelectOption.Key("a")["a"]]
        ]);

        Assert.All(page.FindAll("[data-rask-on-change]"), field =>
        {
            Assert.NotNull(field.Attribute("data-rask-on-change"));
            Assert.Equal("action", field.Attribute("data-rask-bind-on"));
        });
        Assert.Equal(3, page.FindAll("[data-rask-on-change]").Count);
    }

    [Fact]
    public void A_live_checkbox_switch_and_native_select_speak_as_they_are_chosen()
    {
        var m = new Booking();

        var page = Page.Render(() => Form.Model(m)[
            Ui.Checkbox.Bind(() => m.Agreed).Label("Agreed").Live(),
            Ui.Switch.Bind(() => m.Notify).Label("Notify").Live(),
            Ui.Select.Bind(() => m.Plan).Label("Plan").Live()[Ui.SelectOption.Key("a")["a"]]
        ]);

        Assert.All(page.FindAll("[data-rask-on-change]"), field =>
        {
            Assert.NotNull(field.Attribute("data-rask-on-change"));
            Assert.Null(field.Attribute("data-rask-bind-on"));
        });
    }

    [Fact]
    public async Task A_listbox_shows_the_pick_at_once_and_writes_it_with_the_next_action()
    {
        var m = new Booking();
        List<string> after = [];
        var page = Acting.Render(() => Rooms(m, after: after.Add));

        await Pick(page, "Attic");
        var shown = page.TextOf("[data-ui-select-button] [data-value]");
        var (written, called) = (m.Room, after.Count);
        await Acting.Act(page);

        Assert.Equal("Attic", shown);
        Assert.Equal("", written);
        Assert.Equal(0, called);
        Assert.Equal("Attic", m.Room);
        Assert.Equal(["Attic"], after);
    }

    [Fact]
    public async Task A_live_listbox_writes_the_pick_as_it_is_made()
    {
        var m = new Booking();
        List<string> after = [];
        var page = Acting.Render(() => Rooms(m, live: true, after.Add));

        await Pick(page, "Attic");

        Assert.Equal("Attic", m.Room);
        Assert.Equal(["Attic"], after);
    }

    [Fact]
    public async Task A_pick_changed_before_the_action_is_written_once_as_the_last_one()
    {
        var m = new Booking();
        List<string> after = [];
        var page = Acting.Render(() => Rooms(m, after: after.Add));

        await Pick(page, "Basement");
        await Pick(page, "Attic");
        await Acting.Act(page);

        Assert.Equal("Attic", m.Room);
        Assert.Equal(["Attic"], after);
    }

    [Fact]
    public async Task A_rule_of_a_control_that_waits_speaks_with_the_action_and_not_at_the_pick()
    {
        var m = new Booking();
        var page = Acting.Render(() => Form.Model(m)[Rooms(m)]);

        await Pick(page, "Basement");
        var atThePick = page.Render();
        await Acting.Act(page);

        Assert.DoesNotContain("Not the basement.", atThePick, StringComparison.Ordinal);
        Assert.Contains("Not the basement.", page.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_message_under_a_control_that_waits_goes_when_the_reader_picks_again()
    {
        var m = new Booking();
        var page = Acting.Render(() => Form.Model(m)[Rooms(m)]);
        await Pick(page, "Basement");
        await Acting.Act(page);

        await Pick(page, "Attic");

        Assert.DoesNotContain("Not the basement.", page.Render(), StringComparison.Ordinal);
        Assert.Equal("Basement", m.Room);
    }

    [Fact]
    public async Task Two_controls_that_wait_do_not_write_each_other_and_both_arrive_with_the_action()
    {
        var m = new Booking();
        var page = Acting.Render(() => Fragment[
            Rooms(m),
            Ui.Calendar.Bind(() => m.Day).Locale("en-US").On(Today)
        ]);

        await Pick(page, "Attic");
        await page.On(CalendarField).Change("2026-01-20");
        var (room, day) = (m.Room, m.Day);
        await Acting.Act(page);

        Assert.Equal(("", default), (room, day));
        Assert.Equal(("Attic", new DateOnly(2026, 1, 20)), (m.Room, m.Day));
        Assert.Contains("data-date=\"2026-01-20\" data-selected", page.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_group_of_boxes_holds_every_tick_and_writes_them_together()
    {
        var m = new Booking();
        var page = Acting.Render(() =>
            Ui.CheckboxGroup.Bind(() => m.Extras).Id("extras")[
                Ui.Checkbox.Value("Breakfast").Label("Breakfast"),
                Ui.Checkbox.Value("Parking").Label("Parking"),
                Ui.Checkbox.Value("Late").Label("Late")
            ]);

        await page.On("#extras-parking").Change("true");
        await page.On("#extras-breakfast").Change("true");
        var written = m.Extras.Count;
        var ticked = page.FindAll("input[checked]").Count;
        await Acting.Act(page);

        Assert.Equal(0, written);
        Assert.Equal(2, ticked);
        Assert.Equal(["Breakfast", "Parking"], m.Extras);
    }

    [Fact]
    public async Task A_pick_in_a_control_a_render_removed_is_dropped_with_it()
    {
        var m = new Booking();
        var shown = true;
        var page = Acting.Render(() => Div[shown ? Rooms(m) : null]);
        await Pick(page, "Attic");

        shown = false;
        page.Render();
        await Acting.Act(page);

        Assert.Equal("", m.Room);
    }

    [Fact]
    public async Task A_callback_a_waiting_control_raises_into_the_page_finds_the_model_written()
    {
        var door = new CodedDoor();
        var page = Page.Render(door);

        await page.On("input[type=\"hidden\"]").Input("1234");

        Assert.Equal("1234", door.CodeWhenComplete);
    }

    [Fact]
    public async Task A_live_control_writes_what_waited_ahead_of_its_own_value()
    {
        var m = new Booking();
        string? roomWhenTheDayArrived = null;
        var page = Acting.Render(() => Fragment[
            Rooms(m),
            Ui.Calendar.Bind(() => m.Day).Live().AfterBind(_ => roomWhenTheDayArrived = m.Room).Locale("en-US").On(Today)
        ]);
        await Pick(page, "Attic");

        await page.On(CalendarField).Input("2026-01-20");

        Assert.Equal("Attic", roomWhenTheDayArrived);
        Assert.Equal(new DateOnly(2026, 1, 20), m.Day);
    }
}

/// <summary>A page whose one-time code is checked the moment its last cell is filled.</summary>
public sealed partial class CodedDoor : Component
{
    private string Code { get; set; } = "";

    /// <summary>The bound code as the page's own handler found it.</summary>
    public string? CodeWhenComplete { get; private set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ui.Otp.Bind(() => Code).Length(4).OnComplete(_ => CodeWhenComplete = Code);
}
