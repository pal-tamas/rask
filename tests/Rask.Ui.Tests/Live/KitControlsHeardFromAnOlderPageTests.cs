using Rask.Core;
using Rask.TestSupport;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.UiTests.Live;

/// <summary>
///     Every kit control that writes a value, driven faster than the page answers: each event is read from the page
///     as loaded, and each one the page hears is a render — so the last arrives several pages late, and is heard.
/// </summary>
/// <remarks>
///     The guard for the controls whose handlers already read their state when they run (a method of the control,
///     or a closure over the row's own value): a handler that closes over what one render worked out is a new one
///     every render, and an event from an older page then runs nothing (<c>Component.StaleEvents</c>).
/// </remarks>
public sealed partial class KitControlsHeardFromAnOlderPageTests : global::Rask.Core.RaskMarkup
{
    private static readonly string[] Days = ["2026-10-01", "2026-10-02", "2026-10-03", "2026-10-04"];

    [Fact]
    public async Task Date_picker_every_change_field_is_heard_late()
    {
        foreach (var at in Enumerable.Range(0, 3))
        {
            DateOnly day = default;
            using var session = new OlderPage(new StubComponent(() => Ui.DatePicker.Value(day).OnChange(d => day = d).Type(Ui.DatePickerType.Input).Locale("en-US")));
            var ids = session.Ids("change");
            if (at >= ids.Count)
            {
                break;
            }

            var heard = true;
            foreach (var text in Days)
            {
                heard = await session.TypedAsync(ids[at], text);
            }

            Assert.True(heard, $"change field {at} of {ids.Count}");
            Assert.Equal(new DateOnly(2026, 10, 4), day);
        }
    }

    [Fact]
    public async Task Calendar_change_is_heard_late()
    {
        DateOnly day = default;
        using var session = new OlderPage(new StubComponent(() => Ui.Calendar.Value(day).OnChange(d => day = d).Locale("en-US")));
        var id = session.Ids("change")[0];

        var heard = true;
        foreach (var text in Days)
        {
            heard = await session.TypedAsync(id, text);
        }

        Assert.True(heard);
        Assert.Equal(new DateOnly(2026, 10, 4), day);
    }

    [Fact]
    public async Task Datetime_picker_change_is_heard_late()
    {
        DateTime moment = default;
        using var session = new OlderPage(new StubComponent(() => Ui.DatetimePicker.Value(moment).OnChange(d => moment = d).Locale("en-US")));
        var id = session.Ids("change")[0];

        var heard = true;
        foreach (var text in Days)
        {
            heard = await session.TypedAsync(id, text + "T10:30");
        }

        Assert.True(heard);
        Assert.Equal(new DateTime(2026, 10, 4, 10, 30, 0), moment);
    }

    [Fact]
    public async Task Color_picker_change_is_heard_late()
    {
        var color = "#000000";
        using var session = new OlderPage(new StubComponent(() => Ui.ColorPicker.Value(color).OnChange(c => color = c)));
        var id = session.Ids("change")[0];

        var heard = true;
        foreach (var text in new[] { "#111111", "#222222", "#333333", "#444444" })
        {
            heard = await session.TypedAsync(id, text);
        }

        Assert.True(heard);
        Assert.Equal("#444444", color);
    }

    [Fact]
    public async Task Switch_change_is_heard_late()
    {
        var on = false;
        using var session = new OlderPage(new StubComponent(() => Ui.Switch.Value(on).OnChange(v => on = v)));
        var id = session.Id("change");

        var heard = true;
        foreach (var text in new[] { "true", "false", "true", "false", "true" })
        {
            heard = await session.TypedAsync(id, text);
        }

        Assert.True(heard);
        Assert.True(on);
    }

    [Fact]
    public async Task Radio_group_picks_are_heard_late()
    {
        var plan = "a";
        using var session = new OlderPage(new StubComponent(() => Ui.RadioGroup.Value(plan).OnChange(p => plan = p)[
            Ui.Radio.Value("a").Label("A"), Ui.Radio.Value("b").Label("B"), Ui.Radio.Value("c").Label("C"), Ui.Radio.Value("d").Label("D"), Ui.Radio.Value("e").Label("E")]));
        var ids = session.Ids("change");

        var heard = true;
        foreach (var id in ids.Skip(1))
        {
            heard = await session.TypedAsync(id, "on");
        }

        Assert.True(heard);
        Assert.Equal("e", plan);
    }

    [Fact]
    public async Task Checkbox_group_ticks_are_heard_late()
    {
        ICollection<string> picked = [];
        using var session = new OlderPage(new StubComponent(() => Ui.CheckboxGroup.Values(picked).OnChange(p => picked = p)[
            Ui.Checkbox.Value("a").Label("A"), Ui.Checkbox.Value("b").Label("B"), Ui.Checkbox.Value("c").Label("C"), Ui.Checkbox.Value("d").Label("D")]));
        var ids = session.Ids("change");

        var heard = true;
        foreach (var id in ids)
        {
            heard = await session.TypedAsync(id, "true");
        }

        Assert.True(heard);
        Assert.Equal(["a", "b", "c", "d"], picked.Order());
    }

    [Fact]
    public async Task Checkbox_all_turns_are_heard_late()
    {
        ICollection<string> picked = [];
        using var session = new OlderPage(new StubComponent(() => Ui.CheckboxGroup.Values(picked).OnChange(p => picked = p)[
            Ui.CheckboxAll.Label("All"), Ui.Checkbox.Value("a").Label("A"), Ui.Checkbox.Value("b").Label("B")]));
        var all = session.Ids("change")[0];

        var heard = true;
        foreach (var text in new[] { "true", "false", "true" })
        {
            heard = await session.TypedAsync(all, text);
        }

        Assert.True(heard);
        Assert.Equal(["a", "b"], picked.Order());
    }

    [Fact]
    public async Task Toggle_turns_are_heard_late()
    {
        var on = false;
        using var session = new OlderPage(new StubComponent(() => Ui.Toggle.Checked(on).OnChange(v => on = v)["Fast"]));
        var id = session.Id("click");

        var heard = true;
        for (var i = 0; i < 5; i++)
        {
            heard = await session.PressedAsync(id);
        }

        Assert.True(heard);
        Assert.True(on);
    }

    [Fact]
    public async Task Tabs_choices_are_heard_late()
    {
        var tab = "a";
        using var session = new OlderPage(new StubComponent(() => Ui.Tabs.Value(tab).OnChange(t => tab = t)[
            Ui.Tab.Name("a")["A"], Ui.Tab.Name("b")["B"], Ui.Tab.Name("c")["C"], Ui.Tab.Name("d")["D"]]));
        var id = session.Id("change");

        var heard = true;
        foreach (var text in new[] { "b", "c", "d", "a" })
        {
            heard = await session.TypedAsync(id, text);
        }

        Assert.True(heard);
        Assert.Equal("a", tab);
    }

    [Fact]
    public async Task Menu_checkbox_turns_are_heard_late()
    {
        var on = false;
        using var session = new OlderPage(new StubComponent(() => Ui.MenuCheckbox.Value(on).OnChange(v => on = v)["Archived"]));
        var id = session.Id("click");

        var heard = true;
        for (var i = 0; i < 5; i++)
        {
            heard = await session.PressedAsync(id);
        }

        Assert.True(heard);
        Assert.True(on);
    }

    private sealed class Model
    {
        public DateOnly Day { get; set; }
        public DateTime Moment { get; set; }
        public TimeOnly? At { get; set; }
        public string Color { get; set; } = "#000000";
        public bool On { get; set; }
        public string Plan { get; set; } = "a";
        public List<string> Picked { get; set; } = [];
        public string Tab { get; set; } = "a";
        public string? Phone { get; set; }
    }

    private static async Task<bool> All(OlderPage session, string id, IEnumerable<string> values)
    {
        var heard = true;
        foreach (var value in values)
        {
            heard = await session.TypedAsync(id, value);
        }

        return heard;
    }

    [Fact]
    public async Task Bound_date_picker_change_is_heard_late()
    {
        var m = new Model();
        using var session = new OlderPage(new StubComponent(() => Ui.DatePicker.Bind(() => m.Day).Locale("en-US")));

        Assert.True(await All(session, session.Ids("change")[0], Days));
    }

    [Fact]
    public async Task Bound_calendar_live_is_heard_late()
    {
        var m = new Model();
        using var session = new OlderPage(new StubComponent(() => Ui.Calendar.Bind(() => m.Day).Live().Locale("en-US")));

        Assert.True(await All(session, session.Ids("input")[0], Days));
        Assert.Equal(new DateOnly(2026, 10, 4), m.Day);
    }

    [Fact]
    public async Task Bound_color_picker_live_is_heard_late()
    {
        var m = new Model();
        using var session = new OlderPage(new StubComponent(() => Ui.ColorPicker.Bind(() => m.Color).Live()));

        Assert.True(await All(session, session.Ids("input")[0], ["#111111", "#222222", "#333333", "#444444"]));
        Assert.Equal("#444444", m.Color);
    }

    [Fact]
    public async Task Bound_switch_is_heard_late()
    {
        var m = new Model();
        using var session = new OlderPage(new StubComponent(() => Ui.Switch.Bind(() => m.On).Live()));
        var id = session.Ids("change").Concat(session.Ids("input")).First();

        Assert.True(await All(session, id, ["true", "false", "true", "false", "true"]));
        Assert.True(m.On);
    }

    [Fact]
    public async Task Bound_radio_group_is_heard_late()
    {
        var m = new Model();
        using var session = new OlderPage(new StubComponent(() => Ui.RadioGroup.Bind(() => m.Plan).Live()[
            Ui.Radio.Value("a").Label("A"), Ui.Radio.Value("b").Label("B"), Ui.Radio.Value("c").Label("C"), Ui.Radio.Value("d").Label("D"), Ui.Radio.Value("e").Label("E")]));
        var ids = session.Ids("change");

        var heard = true;
        foreach (var id in ids.Skip(1))
        {
            heard = await session.TypedAsync(id, "on");
        }

        Assert.True(heard);
        Assert.Equal("e", m.Plan);
    }

    [Fact]
    public async Task Bound_checkbox_group_is_heard_late()
    {
        var m = new Model();
        using var session = new OlderPage(new StubComponent(() => Ui.CheckboxGroup.Bind(() => m.Picked).Live()[
            Ui.CheckboxAll.Label("All"), Ui.Checkbox.Value("a").Label("A"), Ui.Checkbox.Value("b").Label("B"), Ui.Checkbox.Value("c").Label("C")]));
        var ids = session.Ids("change");

        var heard = true;
        foreach (var id in ids.Skip(1))
        {
            heard = await session.TypedAsync(id, "true");
        }

        heard &= await session.TypedAsync(ids[0], "false");
        heard &= await session.TypedAsync(ids[0], "true");

        Assert.True(heard);
        Assert.Equal(["a", "b", "c"], m.Picked.Order());
    }

    [Fact]
    public async Task Bound_tabs_are_heard_late()
    {
        var m = new Model();
        using var session = new OlderPage(new StubComponent(() => Ui.Tabs.Bind(() => m.Tab)[
            Ui.Tab.Name("a")["A"], Ui.Tab.Name("b")["B"], Ui.Tab.Name("c")["C"], Ui.Tab.Name("d")["D"]]));

        Assert.True(await All(session, session.Id("change"), ["b", "c", "d", "a"]));
        Assert.Equal("a", m.Tab);
    }

    [Fact]
    public async Task Bound_time_picker_is_heard_late()
    {
        var m = new Model();
        using var session = new OlderPage(new StubComponent(() => Ui.TimePicker.Bind(() => m.At).Live().Type(Ui.TimePickerType.Input).Locale("en-US")));
        var ids = session.Ids("change").Concat(session.Ids("input")).ToList();

        Assert.True(await All(session, ids[^1], ["09:00", "10:00", "11:00", "12:00"]), $"{ids.Count} fields");
        Assert.Equal(new TimeOnly(12, 0), m.At);
    }

    [Fact]
    public async Task Bound_time_picker_list_is_heard_late()
    {
        var m = new Model();
        using var session = new OlderPage(new StubComponent(() => Ui.TimePicker.Bind(() => m.At).Live().Locale("en-US")));
        var ids = session.Ids("click");

        var heard = true;
        foreach (var id in ids.Skip(ids.Count - 4))
        {
            heard = await session.PressedAsync(id);
        }

        Assert.True(heard, $"{ids.Count} rows");
        Assert.NotNull(m.At);
    }

    [Fact]
    public async Task Bound_phone_is_heard_late()
    {
        var m = new Model();
        using var session = new OlderPage(new StubComponent(() => Ui.Phone.Bind(() => m.Phone).Live().Country("US")));
        var ids = session.Ids("change").Concat(session.Ids("input")).ToList();

        Assert.True(await All(session, ids[^1], ["+12015550121", "+12015550122", "+12015550123", "+12015550124"]), $"{ids.Count} fields");
        Assert.Equal("+12015550124", m.Phone);
    }

    [Fact]
    public async Task Bound_menu_checkbox_is_heard_late()
    {
        var m = new Model();
        using var session = new OlderPage(new StubComponent(() => Ui.MenuCheckbox.Bind(() => m.On).Live()["Archived"]));
        var id = session.Id("click");

        var heard = true;
        for (var i = 0; i < 5; i++)
        {
            heard = await session.PressedAsync(id);
        }

        Assert.True(heard);
        Assert.True(m.On);
    }
}
