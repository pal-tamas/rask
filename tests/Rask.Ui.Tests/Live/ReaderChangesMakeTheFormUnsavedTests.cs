using System.Globalization;
using Rask.Core;
using Rask.UiTests.Flux;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Live;

/// <summary>
///     A form that asks before it is left (<c>ConfirmLeave</c>) is unsaved from the reader's first change — and
///     a control the kit draws in C# changes on a press the browser raises no <c>input</c> for. The page hears
///     that press, so the page says it: the form carries <c>data-rask-edited</c> until an accepted submit has
///     carried the value (<c>ReaderEdits</c>, read by <c>rask-leave.ts</c>).
/// </summary>
/// <remarks>
///     One row per control the kit draws, and <see cref="Every_control_that_chooses_is_in_the_table_or_heard_by_the_browser" />
///     fails for a control that is in neither list: a new one cannot forget to say what the reader did.
/// </remarks>
public partial class ReaderChangesMakeTheFormUnsavedTests : global::Rask.Core.RaskMarkup
{
    private const string Edited = "data-rask-edited";

    private static readonly DateOnly Today = new(2026, 1, 15);

    private static readonly string[] Words = ["Alpha", "Beta", "Gamma"];

    private static readonly Dictionary<string, (Type Control, Func<Answers, Component> Draw, Func<Page, Task> Change)> Controls = new(StringComparer.Ordinal)
    {
        ["listbox"] = (typeof(UiSelect<>), a => Ui.Select.Bind(() => a.One).Listbox[Options()], PickOption),
        ["listbox, live"] = (typeof(UiSelect<>), a => Ui.Select.Bind(() => a.One).Listbox.Live()[Options()], PickOption),
        ["listbox the page holds"] = (typeof(UiSelect<>), a => Ui.Select.Value(a.One).OnChange(one => a.One = one).Listbox[Options()], PickOption),
        ["listbox, cleared"] = (typeof(UiSelect<>), a => Ui.Select.Bind(() => a.Picked).Listbox.Clearable()[Options()], page => page.On("button[aria-label=\"Clear selected\"]").Click()),
        ["combobox"] = (typeof(UiSelect<>), a => Ui.Select.Bind(() => a.One).Combobox[Options()], PickOption),
        ["multiple listbox"] = (typeof(UiSelectMultiple<>), a => Ui.Select.Bind(() => a.Many).Listbox.Multiple()[Options()], PickOption),
        ["pillbox"] = (typeof(UiPillbox<>), a => Ui.Pillbox.Bind(() => a.Many)[Words.Select(word => Ui.PillboxOption.Key(word)[word])], page => First(page, "[data-ui-listbox-option]", "click")),
        ["pillbox, a pill removed"] = (typeof(UiPillbox<>), a => Ui.Pillbox.Bind(() => a.Chosen)[Words.Select(word => Ui.PillboxOption.Key(word)[word])], page => page.On("[data-value=\"Alpha\"] [data-rask-on-click]").Click()),
        ["calendar"] = (typeof(UiCalendar), a => Ui.Calendar.Bind(() => a.Day).Locale("en-US").On(Today), page => page.On(Day(20)).Click()),
        ["calendar, multiple"] = (typeof(UiCalendarMultiple), a => Ui.Calendar.Multiple.Bind(() => a.Days).Locale("en-US").On(Today), page => page.On(Day(20)).Click()),
        ["date picker"] = (typeof(UiDatePicker), a => Ui.DatePicker.Bind(() => a.Day).Locale("en-US").On(Today), page => page.On(Day(20)).Click()),
        ["time picker"] = (typeof(UiTimePicker<>), a => Ui.TimePicker.Bind(() => a.At), page => First(page, "button[role=\"option\"]", "click")),
        ["colour picker"] = (typeof(UiColorPicker), a => Ui.ColorPicker.Bind(() => a.Text), page => First(page, "input[type=\"hidden\"][data-rask-on-change]", "change", "#22c55e")),
        ["slider"] = (typeof(UiSlider<>), a => Ui.Slider.Bind(() => a.Volume), page => First(page, "input[type=\"range\"]", "input", "40")),
        ["rating"] = (typeof(UiRating), a => Ui.Rating.Bind(() => a.Stars).Group("stars").Label("Stars").Max(5), page => Last(page, "input[type=\"radio\"]", "change", "on")),
        ["otp"] = (typeof(UiOtp), a => Ui.Otp.Bind(() => a.Text).Length(6), page => First(page, "input[type=\"hidden\"][data-rask-on-input]", "input", "12")),
        ["checkbox group"] = (typeof(UiCheckboxGroup<>), a => Ui.CheckboxGroup.Bind(() => a.Many).Id("many")[Words.Select(word => Ui.Checkbox.Key(word).Value(word).Label(word))], page => page.On("#many-beta").Change("true")),
        ["radio group"] = (typeof(UiRadioGroup<>), a => Ui.RadioGroup.Bind(() => a.One).Name("one")[Words.Select(word => Ui.Radio.Key(word).Value(word).Label(word))], page => First(page, "input[type=\"radio\"]", "change", "on")),
        ["filter"] = (typeof(UiFilter<>), a => Ui.Filter.Bind(() => a.One).Group("one").Options([.. Words.Select(word => ((string?)word, word))]), page => Last(page, "input[type=\"radio\"]", "change", "on")),
        ["menu checkbox"] = (typeof(UiMenuCheckbox), a => Ui.Menu[Ui.MenuCheckbox.Bind(() => a.Alerts)["Alerts"]], page => page.On("[data-rask-on-click]").Click()),
        ["menu radio group"] = (typeof(UiMenuRadioGroup<>), a => Ui.Menu[Ui.MenuRadioGroup.Bind(() => a.One)[Words.Select(word => Ui.MenuRadio.Key(word).Value(word)[word])]], page => First(page, "[data-rask-on-click]", "click")),
        ["toggle"] = (typeof(UiToggle), a => Ui.Toggle.Bind(() => a.Alerts).Icon(Ui.IconName.Bolt), page => page.On("[role=\"switch\"]").Click()),
        ["file item, removed"] = (typeof(UiFileItemRemove), _ => Ui.FileItemRemove.OnClick(() => { }), page => page.On("[data-ui-file-item-remove]").Click()),
    };

    // Heard by the browser itself: one native element whose `input` and `change` the guard counts, or — the
    // editor — a contenteditable whose `input` it counts and a commit that says so besides. A part answers
    // through its group, and which tab is open is no value of the form's.
    private static readonly Dictionary<Type, string> HeardElsewhere = new()
    {
        [typeof(UiCheckbox)] = "a native checkbox: the browser's own change",
        [typeof(UiAutocomplete)] = "the kit's text input: a pick is written into it in the browser, which raises its input and change",
        [typeof(UiSwitch)] = "a native checkbox: the browser's own change",
        [typeof(UiRadio)] = "a native radio: the browser's own change",
        [typeof(UiMenuRadio)] = "a part of UiMenuRadioGroup, which commits",
        [typeof(UiCalendarRange)] = "UiCalendarControl's one commit, as the calendar rows show",
        [typeof(UiDatePickerRange)] = "UiDatePickerControl's one commit, as the date picker row shows",
        [typeof(UiEditor)] = "needs the browser's editor: EditorOnServerTests leaves a form it typed into",
        [typeof(UiTabs)] = "the open tab is the page's drawing, not the form's data",
        [typeof(UiTab)] = "a part of UiTabs",
    };

    public static TheoryData<string> Names => [.. Controls.Keys];

    private static string Day(int day) =>
        "button[aria-label=\"" + new DateOnly(2026, 1, day).ToString("D", CultureInfo.GetCultureInfo("en-US")) + "\"]";

    private static IEnumerable<Component> Options() => Words.Select(word => Ui.SelectOption.Key(word).Value(word)[word]);

    private static Task PickOption(Page page) => First(page, "[data-ui-option]", "click");

    // The first (or last) element a selector finds that hears `domEvent`, told `value` when there is one.
    private static Task First(Page page, string selector, string domEvent, string? value = null) =>
        Tell(page.FindAll(selector)[0], page, domEvent, value);

    private static Task Last(Page page, string selector, string domEvent, string? value = null) =>
        Tell(page.FindAll(selector)[^1], page, domEvent, value);

    private static Task Tell(Rask.Testing.HtmlNode heard, Page page, string domEvent, string? value) =>
        page.Invoke(
            heard.Attribute("data-rask-on-" + domEvent) ?? throw new InvalidOperationException($"<{heard.Path()}> hears no {domEvent}."),
            value is null ? "{}" : "{\"value\":\"" + value + "\"}");

    private static Page Draw(Answers answers, Func<Answers, Component> control, Action? save = null) =>
        Page.Render(() => Div[
            Form.Model(answers).OnSubmit(() => save?.Invoke()).ConfirmLeave("Leave without saving?")[
                Ui.Input.Bind(() => answers.Before),
                control(answers),
                Ui.Button.Primary.Submit["Save"]
            ]
        ]);

    [Theory]
    [MemberData(nameof(Names))]
    public async Task What_the_reader_chooses_in_a_drawn_control_marks_its_form_edited(string name)
    {
        var page = Draw(new Answers(), Controls[name].Draw);
        var before = page.Find("form").Attribute(Edited);

        await Controls[name].Change(page);

        Assert.Null(before);
        Assert.NotNull(page.Find("form").Attribute(Edited));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task A_submit_the_form_accepts_carries_what_was_chosen_and_takes_the_mark_away(string name)
    {
        var page = Draw(new Answers(), Controls[name].Draw);
        await Controls[name].Change(page);

        await page.On("form").Submit();

        Assert.Null(page.Find("form").Attribute(Edited));
        Assert.Equal("1", page.Find("form").Attribute("data-rask-saved"));
    }

    [Fact]
    public async Task A_submit_the_rules_refuse_leaves_the_form_edited()
    {
        var page = Draw(new Answers { Before = null! }, Controls["listbox"].Draw);
        await PickOption(page);

        await page.On("form").Submit();

        Assert.NotNull(page.Find("form").Attribute(Edited));
        Assert.Null(page.Find("form").Attribute("data-rask-saved"));
    }

    [Fact]
    public async Task A_choice_made_after_a_save_marks_the_form_again()
    {
        var page = Draw(new Answers(), Controls["listbox"].Draw);
        await PickOption(page);
        await page.On("form").Submit();

        await PickOption(page);

        Assert.NotNull(page.Find("form").Attribute(Edited));
    }

    [Fact]
    public void A_value_the_page_writes_by_itself_marks_nothing()
    {
        var answers = new Answers();
        var page = Draw(answers, Controls["listbox"].Draw);

        answers.One = "Gamma";
        page.Render();

        Assert.Equal("Gamma", page.TextOf("[data-ui-select-button] [data-value]"));
        Assert.Null(page.Find("form").Attribute(Edited));
    }

    [Fact]
    public async Task Searching_a_list_marks_nothing_and_its_field_says_it_holds_none_of_the_forms_data()
    {
        var page = Draw(new Answers(), a => Ui.Select.Bind(() => a.One).Listbox.Searchable()[Ui.SelectSearch.OnInput(_ => { }), Options()]);

        await page.On("[data-ui-select-search] input").Input("Ga");

        Assert.Null(page.Find("form").Attribute(Edited));
        Assert.NotNull(page.Find("[data-ui-select-search] input").Attribute("data-rask-unguarded"));
    }

    [Fact]
    public void A_toggle_that_is_neither_bound_nor_listened_to_is_the_browsers_own_and_no_data_of_the_form()
    {
        var page = Draw(new Answers(), _ => Ui.Toggle.Icon(Ui.IconName.Bolt));

        var toggle = page.Find("[role=\"switch\"]");

        Assert.Null(toggle.Attribute("data-rask-on-click"));
        Assert.Null(page.Find("form").Attribute(Edited));
    }

    [Fact]
    public async Task A_form_that_does_not_ask_before_leaving_is_not_marked()
    {
        var answers = new Answers();
        var page = Page.Render(() => Form.Model(answers).OnSubmit(() => { })[Ui.Select.Bind(() => answers.One).Listbox[Options()]]);

        await PickOption(page);

        Assert.Null(page.Find("form").Attribute(Edited));
    }

    [Fact]
    public void Every_control_that_chooses_is_in_the_table_or_heard_by_the_browser()
    {
        var chooses = typeof(UiFormCommit).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.GetInterfaces().Any(i => i.Name == "IDeferredBind"));
        var known = Controls.Values.Select(row => row.Control).Concat(HeardElsewhere.Keys).ToHashSet();

        var forgotten = chooses.Where(type => !known.Contains(type)).Select(type => type.Name);

        Assert.Empty(forgotten);
    }

    private sealed class Answers
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string Before { get; set; } = "before";

        public string Text { get; set; } = string.Empty;

        public bool Alerts { get; set; }

        public string? One { get; set; }

        public string? Picked { get; set; } = "Alpha";

        public List<string> Many { get; set; } = [];

        public List<string> Chosen { get; set; } = ["Alpha"];

        public DateOnly Day { get; set; }

        public List<DateOnly> Days { get; set; } = [];

        public TimeOnly? At { get; set; }

        public int Volume { get; set; }

        public int Stars { get; set; }
    }
}
